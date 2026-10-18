using System.Net;
using Common.Application.Customers;
using Common.Application.EventCatalog;
using Common.Application.Fees;
using Common.Application.Payments;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Common.IntegrationEvents.Payments;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Checkout;
using Ticketing.Domain.Common;
using Ticketing.Domain.Orders;
using Common.Domain.Phones;
using Common.Domain.Codes;

namespace Ticketing.Application.Orders;

/// <summary>
///     UF-003 checkout: price the selection, hold capacity and place the order in one transaction, then send the
///     M-Pesa prompt. Every POST carries an <c>Idempotency-Key</c> (Spec 08 §11): a repeat returns the same order and
///     never a second prompt.
/// </summary>
public sealed class CheckoutService(
    IEventCatalog events,
    ICustomerDirectory customers,
    IOrderRepository orders,
    ITicketRepository tickets,
    IWaitlistRepository waitlist,
    ICapacityLedger capacity,
    ITicketingUnitOfWork unitOfWork,
    IMobilePayments payments,
    IOptions<OrderOptions> options,
    IOptions<LipaMdogoMdogoOptions> installmentOptions,
    IOptions<PlatformFeeOptions> feeOptions,
    TimeProvider time)
{
    private const int MaxReferenceAttempts = 5;

    public async Task<Result<OrderView>> PlaceOrderAsync(
        ApplicationUserId customerId,
        string? idempotencyKey,
        PlaceOrderRequest request,
        string callbackHost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = idempotencyKey?.Trim() ?? string.Empty;
        if (key.Length is 0 or > Order.IdempotencyKeyMaxLength)
        {
            return TicketingErrors.Invalid("IdempotencyKeyRequired", ["Idempotency-Key: Send a unique key (at most 64 characters) with every order."]);
        }

        var repeat = await orders.FindByIdempotencyKeyAsync(customerId.Value, key, cancellationToken);
        if (repeat is not null)
        {
            return await ViewAsync(repeat, cancellationToken);
        }

        var customer = await CustomerAsync(customerId, cancellationToken);
        if (customer.IsFailure)
        {
            return customer.Error!;
        }

        var payer = ResolvePayer(request.PayerPhone, customer.Value!.Phone);
        if (payer.IsFailure)
        {
            return payer.Error!;
        }

        var found = string.IsNullOrWhiteSpace(request.EventSlug)
            ? null
            : await events.FindBySlugAsync(request.EventSlug.Trim(), cancellationToken);
        var sale = found is null || found.Status == EventLifecycle.Draft
            ? null
            : await events.GetSaleSnapshotAsync(found.EventId, cancellationToken);

        if (sale is null)
        {
            return TicketingErrors.EventNotFound;
        }

        var now = time.GetUtcNow();
        var settings = options.Value;

        var open = await orders.ListOpenForCustomerAsync(customerId.Value, sale.EventId, now, cancellationToken);
        if (open.Count >= settings.MaxOpenOrdersPerCustomer)
        {
            var error = TicketingErrors.Conflict("OrderAlreadyOpen", "You already have an order waiting for payment for this event.");
            return new Error
            {
                Key = error.Key,
                Description = error.Description,
                StatusCode = error.StatusCode,
                Value = open[0].Id.Value
            };
        }

        var priced = CheckoutPricing.Price(
            sale,
            [.. (request.Items ?? []).Select(x => new CheckoutItem(x.TierId, x.OfferId, x.Quantity))],
            now);

        if (priced.IsFailure)
        {
            return priced.Error!;
        }

        var pricedOrder = priced.Value!;

        var waitlisted = await waitlist.ExistsAsync(sale.EventId, customer.Value.Phone, cancellationToken);

        if (pricedOrder.RequiresEarlyAccess && !waitlisted)
        {
            return new Error
            {
                Key = "Ticketing.EarlyAccessOnly",
                Description = "Early access is for phone numbers on the waitlist. General sale opens later.",
                StatusCode = HttpStatusCode.Forbidden
            };
        }

        var installments = InstallmentTerms(request.PaymentPlan, sale, pricedOrder, now);
        if (installments.IsFailure)
        {
            return installments.Error!;
        }

        var reference = await NewReferenceAsync(cancellationToken);
        var orderId = new OrderId(Guid.CreateVersion7(now));

        var placed = Order.Place(
            orderId,
            sale.EventId,
            reference,
            customer.Value,
            payer.Value!,
            key,
            [.. pricedOrder.Lines.Select(x => OrderLine.Create(new OrderLineId(Guid.CreateVersion7(now)), x))],
            sale.Policies.HoldMinutes,
            now,
            installments.Value,
            TicketFee(feeOptions.Value, pricedOrder.Admissions, waitlisted));

        if (placed.IsFailure)
        {
            return placed.Error!;
        }

        var order = placed.Value!;

        var held = await unitOfWork.InTransactionAsync<bool>(
            async ct =>
            {
                var hold = await capacity.HoldAsync(order.Id, pricedOrder.Claims, order.HoldExpiresAt, ct);
                if (hold.IsFailure)
                {
                    return hold.Error!;
                }

                orders.Add(order);
                var saved = await unitOfWork.SaveChangesAsync(ct);
                if (saved.IsFailure)
                {
                    return saved.Error!;
                }

                return true;
            },
            cancellationToken);

        if (held.IsFailure)
        {
            return held.Error!;
        }

        return await StartPaymentAsync(order, payer.Value!, callbackHost, cancellationToken);
    }

    /// <summary>Sends a new prompt for an order still awaiting payment (the last one failed or was dismissed).</summary>
    public async Task<Result<OrderView>> RetryPaymentAsync(
        ApplicationUserId customerId,
        OrderId orderId,
        RetryPaymentRequest request,
        string callbackHost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var order = await orders.FindAsync(orderId, cancellationToken);
        if (order is null || order.CustomerUserId != customerId.Value)
        {
            return TicketingErrors.NotFound("OrderNotFound", "The order was not found.");
        }

        if (order.CurrentPaymentId is { } current &&
            await payments.FindAsync(current, cancellationToken) is { Status: MobilePaymentStatus.Pending })
        {
            return TicketingErrors.Conflict("PaymentInProgress", "An M-Pesa prompt is still waiting on the phone. Complete or cancel it first.");
        }

        if (order.Status == OrderStatus.PartiallyPaid && order.BalanceDueAt is { } due &&
            time.GetUtcNow() > due.AddHours(installmentOptions.Value.OverdueGraceHours))
        {
            return TicketingErrors.Conflict("BalanceOverdue", "The balance is overdue and the order is being cancelled.");
        }

        var payer = ResolvePayer(request.PayerPhone, order.PayerPhone);
        if (payer.IsFailure)
        {
            return payer.Error!;
        }

        return await StartPaymentAsync(order, payer.Value!, callbackHost, cancellationToken);
    }

    /// <summary>The signed-in customer's orders, newest first (all events).</summary>
    public async Task<Result<IReadOnlyList<OrderView>>> ListMineAsync(ApplicationUserId customerId, CancellationToken cancellationToken)
    {
        var list = await orders.ListForCustomerAsync(customerId.Value, 50, cancellationToken);
        var now = time.GetUtcNow();

        // Payment status is fetched per order only on the detail endpoint; the list stays one query.
        return Result<IReadOnlyList<OrderView>>.Success([.. list.Select(x => OrderView.From(x, null, x.Status is OrderStatus.Paid or OrderStatus.PartiallyPaid ? x.Admissions : 0, now))]);
    }

    public async Task<Result<OrderView>> GetAsync(ApplicationUserId customerId, OrderId orderId, CancellationToken cancellationToken)
    {
        var order = await orders.FindForReadAsync(orderId, cancellationToken);

        return order is null || order.CustomerUserId != customerId.Value
            ? TicketingErrors.NotFound("OrderNotFound", "The order was not found.")
            : await ViewAsync(order, cancellationToken);
    }

    private async Task<Result<OrderView>> StartPaymentAsync(
        Order order,
        string payerPhone,
        string callbackHost,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var paymentId = Guid.CreateVersion7(now);
        var balance = order.Status == OrderStatus.PartiallyPaid;

        var started = order.StartPayment(paymentId, options.Value.MaxPaymentAttempts, now);
        if (started.IsFailure)
        {
            return started.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        var payment = await payments.StartAsync(
            new StartMobilePayment(
                paymentId,
                balance ? PaymentPurpose.TicketInstallment : PaymentPurpose.TicketOrder,
                order.Id.Value,
                order.AmountDueNow,
                payerPhone,
                order.Reference,
                balance ? $"Balance {order.Reference[..5]}" : $"Order {order.Reference[..6]}",
                callbackHost),
            CancellationToken.None);

        if (payment.Status == MobilePaymentStatus.Failed)
        {
            order.RecordPaymentFailed(paymentId, payment.ErrorKey ?? payment.FailureReason?.ToString() ?? "Failed");
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        return OrderView.From(order, payment, 0, time.GetUtcNow());
    }

    /// <summary>KES <see cref="PlatformFeeOptions.TicketFeePerTicket" /> per admission; waitlisted phones pay none.</summary>
    public static OrderFee TicketFee(PlatformFeeOptions fees, int admissions, bool waitlisted)
    {
        ArgumentNullException.ThrowIfNull(fees);

        if (fees.TicketFeePerTicket == 0)
        {
            return OrderFee.None;
        }

        return waitlisted && fees.WaiveForWaitlist
            ? new OrderFee(0m, true)
            : new OrderFee(fees.TicketFeePerTicket * (decimal)admissions, false);
    }

    /// <summary>Lipa Mdogo Mdogo: offered when the event allows it and the balance due date is far enough away.</summary>
    private Result<LipaMdogoMdogoTerms?> InstallmentTerms(
        PaymentPlan plan,
        EventSaleSnapshot sale,
        PricedOrder priced,
        DateTimeOffset now)
    {
        if (plan == PaymentPlan.Full)
        {
            return Result<LipaMdogoMdogoTerms?>.Success(null);
        }

        if (plan != PaymentPlan.LipaMdogoMdogo)
        {
            return TicketingErrors.Invalid("PaymentPlanInvalid", ["PaymentPlan: Choose Full or LipaMdogoMdogo."]);
        }

        var terms = installmentOptions.Value;
        var dateIds = priced.Lines.Select(x => x.EventDateId).ToHashSet();
        var firstDate = sale.Dates.Where(x => dateIds.Contains(x.DateId)).Min(x => x.StartsAt);
        var dueAt = firstDate.AddDays(-terms.BalanceDueDaysBeforeEvent);

        if (!sale.Policies.LipaMdogoMdogoEnabled || now > dueAt.AddDays(-terms.MinDaysBeforeDue))
        {
            return TicketingErrors.Conflict("LipaMdogoMdogoUnavailable", "Lipa Mdogo Mdogo is not available for this event now. Pay in full instead.");
        }

        return new LipaMdogoMdogoTerms(terms.DepositPercent, dueAt);
    }

    private async Task<Result<OrderView>> ViewAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = order.CurrentPaymentId is { } paymentId ? await payments.FindAsync(paymentId, cancellationToken) : null;
        var issued = order.Status is OrderStatus.Paid or OrderStatus.PartiallyPaid
            ? await tickets.CountForOrderAsync(order.Id, cancellationToken)
            : 0;

        return OrderView.From(order, payment, issued, time.GetUtcNow());
    }

    private async Task<Result<OrderCustomer>> CustomerAsync(ApplicationUserId customerId, CancellationToken cancellationToken)
    {
        var profile = await customers.FindAsync(customerId, cancellationToken);

        if (profile is null)
        {
            return new Error
            {
                Key = "Ticketing.CustomerNotFound",
                Description = "Sign in on this site to buy tickets.",
                StatusCode = HttpStatusCode.Forbidden
            };
        }

        return profile.Phone is null || !KenyanMobile.TryCreate(profile.Phone, out var phone)
            ? TicketingErrors.Conflict("PhoneRequired", "Tickets are issued to a Kenyan mobile number. Sign in with your phone.")
            : new OrderCustomer(customerId.Value, phone!.Value, profile.DisplayName);
    }

    private static Result<string> ResolvePayer(string? requested, string fallback)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return fallback;
        }

        return KenyanMobile.TryCreate(requested, out var phone)
            ? phone!.Value
            : TicketingErrors.Invalid("PayerPhoneInvalid", ["PayerPhone: Enter a Kenyan M-Pesa number, e.g. 0712 345 678."]);
    }

    private async Task<string> NewReferenceAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxReferenceAttempts; attempt++)
        {
            var reference = ShortCode.New(Order.ReferenceLength);
            if (!await orders.ReferenceExistsAsync(reference, cancellationToken))
            {
                return reference;
            }
        }

        throw new InvalidOperationException("Could not generate a unique order reference.");
    }
}
