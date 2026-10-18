using System.Net;
using Commerce.Application.Abstractions;
using Commerce.Application.Krafts;
using Commerce.Application.Menus;
using Commerce.Application.Stock;
using Commerce.Domain.Common;
using Commerce.Domain.Krafts;
using Commerce.Domain.Menus;
using Commerce.Domain.Orders;
using Common.Application.Customers;
using Common.Application.EventCatalog;
using Common.Application.Fees;
using Common.Application.Payments;
using Common.Application.Ticketing;
using Common.Domain.Codes;
using Common.Domain.Phones;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Common.IntegrationEvents.Payments;
using Microsoft.Extensions.Options;

namespace Commerce.Application.Orders;

/// <summary>
///     Food and drink checkout (UF-007, UF-008): the cart is split into one order per Kraft (D-03), every Kraft's
///     portions are held in one transaction, and the customer then pays the orders one at a time, each with its own
///     M-Pesa prompt into that Kraft's till. Every checkout carries an <c>Idempotency-Key</c>: a repeat returns the same
///     orders. A live order or reservation needs a valid ticket for the event (O-04).
/// </summary>
public sealed class KraftCheckoutService(
    IEventCatalog events,
    ICustomerDirectory customers,
    ITicketHolders ticketHolders,
    IWaitlistMembership waitlist,
    IKraftRepository krafts,
    IMenuItemRepository menu,
    IKraftOrderRepository orders,
    IStockLedger stock,
    ICommerceUnitOfWork unitOfWork,
    KraftTills tills,
    IMobilePayments payments,
    IOptions<CommerceOptions> options,
    IOptions<PlatformFeeOptions> feeOptions,
    TimeProvider time)
{
    private const int MaxReferenceAttempts = 5;

    public async Task<Result<CheckoutView>> CheckoutAsync(
        ApplicationUserId customerId,
        string? idempotencyKey,
        CheckoutRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = idempotencyKey?.Trim() ?? string.Empty;
        if (key.Length is 0 or > KraftOrder.IdempotencyKeyMaxLength)
        {
            return CommerceErrors.Invalid("IdempotencyKeyRequired", ["Idempotency-Key: Send a unique key (at most 64 characters) with every checkout."]);
        }

        var repeat = await orders.ListByIdempotencyKeyAsync(customerId.Value, key, cancellationToken);
        if (repeat.Count > 0)
        {
            return await GroupViewAsync(repeat, cancellationToken);
        }

        var customer = await CustomerAsync(customerId, cancellationToken);
        if (customer.IsFailure)
        {
            return customer.Error!;
        }

        var payer = ResolvePayer(request.PayerPhone, customer.Value.Phone);
        if (payer.IsFailure)
        {
            return payer.Error!;
        }

        var snapshot = string.IsNullOrWhiteSpace(request.EventSlug)
            ? null
            : await events.FindBySlugAsync(request.EventSlug.Trim(), cancellationToken);
        if (snapshot is null || snapshot.Status == EventLifecycle.Draft)
        {
            return CommerceErrors.EventNotFound;
        }

        var settings = options.Value;
        var now = time.GetUtcNow();
        var windows = OrderingWindows.For(snapshot, settings);

        if (!Enum.IsDefined(request.Kind))
        {
            return CommerceErrors.Invalid("CheckoutInvalid", ["Kind: Choose Live or Reservation."]);
        }

        var open = request.Kind == KraftOrderKind.Live
            ? windows.LiveOpenAt(now, snapshot.Status)
            : windows.ReservationsOpenAt(now, snapshot.Status);
        if (!open)
        {
            return request.Kind == KraftOrderKind.Live
                ? CommerceErrors.Conflict("LiveOrderingClosed", "Live ordering is open on the day of the event only.")
                : CommerceErrors.Conflict("ReservationsClosed", "Reservations for this event are closed.");
        }

        var ticketCheck = await CheckTicketAsync(request.Kind, customer.Value.Phone, snapshot.EventId, now, settings, cancellationToken);
        if (ticketCheck.IsFailure)
        {
            return ticketCheck.Error!;
        }

        var lines = await PriceAsync(request, snapshot.EventId, settings, cancellationToken);
        if (lines.IsFailure)
        {
            return lines.Error!;
        }

        var (feeEach, feeWaived) = await ServiceFeeAsync(snapshot.EventId, customer.Value.Phone, cancellationToken);
        var groupId = Guid.CreateVersion7(now);
        var holdExpiresAt = now.AddMinutes(settings.HoldMinutes);
        var placed = new List<(KraftOrder Order, IReadOnlyList<StockClaim> Claims)>();

        foreach (var kraftLines in lines.Value!.GroupBy(x => x.Kraft.Id.Value).OrderBy(x => x.First().Kraft.Name, StringComparer.OrdinalIgnoreCase))
        {
            var kraft = kraftLines.First().Kraft;
            var terms = kraft.AtEvent(snapshot.EventId)!;

            // The service fee is for food and drinks: an order of activity bookings only carries none.
            var activitiesOnly = kraftLines.All(x => x.Item.IsActivity);
            var serviceFee = activitiesOnly ? 0m : feeEach;

            var order = KraftOrder.Place(
                new KraftOrderId(Guid.CreateVersion7(now)),
                new KraftOrderSpec(
                    groupId,
                    kraft.Id.Value,
                    kraft.Name,
                    snapshot.EventId,
                    request.Kind,
                    await NewReferenceAsync(cancellationToken),
                    ShortCode.New(KraftOrder.PickupCodeLength),
                    customerId.Value,
                    customer.Value.Phone,
                    customer.Value.Name,
                    payer.Value!,
                    key,
                    terms.CommissionPercent,
                    now,
                    holdExpiresAt,
                    serviceFee,
                    feeWaived && !activitiesOnly,
                    terms.PaysIntoPlatformTill),
                [
                    .. kraftLines.Select(x => KraftOrderLine.Create(
                        new KraftOrderLineId(Guid.CreateVersion7(now)),
                        x.Item.Id.Value,
                        x.Item.Name,
                        x.Quantity,
                        x.Item.Price + x.AddOns.Sum(a => a.Price),
                        x.Item.AgeRestricted,
                        x.AddOns.Count == 0 ? null : string.Join(", ", x.AddOns.Select(a => a.Name))))
                ]);

            var stockKind = request.Kind == KraftOrderKind.Live ? StockKind.Live : StockKind.Reservation;
            IReadOnlyList<StockClaim> claims =
            [
                .. kraftLines.GroupBy(x => x.Item.Id.Value).Select(g => new StockClaim(
                    stockKind,
                    g.Key,
                    snapshot.EventId,
                    kraft.Id.Value,
                    request.Kind == KraftOrderKind.Live ? g.First().Item.LiveStock : g.First().Item.ReservationAllocation,
                    g.Sum(x => x.Quantity),
                    $"{kraft.Name}: {g.First().Item.Name}"))
            ];

            placed.Add((order, claims));
        }

        var held = await unitOfWork.InTransactionAsync<bool>(
            async ct =>
            {
                var empty = new List<string>();

                foreach (var (order, claims) in placed)
                {
                    var hold = await stock.HoldAsync(order.Id.Value, claims, holdExpiresAt, ct);
                    if (hold.IsFailure)
                    {
                        empty.AddRange(hold.Error!.SubErrors ?? []);
                    }
                }

                if (empty.Count > 0)
                {
                    return new Error
                    {
                        Key = "Commerce.SoldOut",
                        Description = "Some of the items you chose are sold out or have fewer portions left.",
                        StatusCode = HttpStatusCode.Conflict,
                        SubErrors = empty
                    };
                }

                orders.AddRange(placed.Select(x => x.Order));
                var saved = await unitOfWork.SaveChangesAsync(ct);

                return saved.IsFailure ? saved.Error! : Result<bool>.Success(true);
            },
            cancellationToken);

        if (held.IsFailure)
        {
            return held.Error!;
        }

        return await GroupViewAsync([.. placed.Select(x => x.Order)], cancellationToken);
    }

    /// <summary>
    ///     Sends the M-Pesa prompt for one order of a checkout, into its Kraft's till. Only one prompt of a checkout can
    ///     be on the phone at a time; pay the next order after this one finishes.
    /// </summary>
    public async Task<Result<KraftOrderView>> PayAsync(
        ApplicationUserId customerId,
        KraftOrderId orderId,
        PayOrderRequest request,
        string callbackHost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var order = await orders.FindAsync(orderId, cancellationToken);
        if (order is null || order.CustomerUserId != customerId.Value)
        {
            return OrderNotFound;
        }

        foreach (var sibling in await orders.ListGroupAsync(order.CheckoutGroupId, cancellationToken))
        {
            if (sibling.Status == KraftOrderStatus.Ordered && sibling.CurrentPaymentId is { } siblingPayment &&
                await payments.FindAsync(siblingPayment, cancellationToken) is { Status: MobilePaymentStatus.Pending })
            {
                return CommerceErrors.Conflict("PaymentInProgress", "An M-Pesa prompt is still waiting on the phone. Complete or cancel it first.");
            }
        }

        var payer = ResolvePayer(request.PayerPhone, order.PayerPhone);
        if (payer.IsFailure)
        {
            return payer.Error!;
        }

        var seller = await krafts.FindForReadAsync(new KraftId(order.KraftId), cancellationToken);
        var (canPay, till) = seller is null ? (false, null) : await tills.DestinationAsync(seller, order.EventId, cancellationToken);
        if (!canPay)
        {
            return CommerceErrors.Conflict("KraftNotTakingPayments", $"{order.KraftName} is not taking payments right now.");
        }

        var now = time.GetUtcNow();
        var paymentId = Guid.CreateVersion7(now);

        var started = order.StartPayment(paymentId, payer.Value!, options.Value.MaxPaymentAttempts, now);
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
                PaymentPurpose.KraftOrder,
                order.Id.Value,
                order.Total,
                payer.Value!,
                order.Reference,
                $"Food {order.Reference}",
                callbackHost,
                till),
            CancellationToken.None);

        if (payment.Status == MobilePaymentStatus.Failed)
        {
            order.PaymentFailed(paymentId, payment.ErrorKey ?? payment.FailureReason?.ToString() ?? "Failed");
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        return KraftOrderView.From(order, payment, time.GetUtcNow());
    }

    public async Task<Result<CheckoutView>> GetCheckoutAsync(ApplicationUserId customerId, Guid checkoutGroupId, CancellationToken cancellationToken)
    {
        var group = await orders.ListGroupAsync(checkoutGroupId, cancellationToken);

        return group.Count == 0 || group[0].CustomerUserId != customerId.Value
            ? CommerceErrors.NotFound("CheckoutNotFound", "The checkout was not found.")
            : await GroupViewAsync(group, cancellationToken);
    }

    public async Task<Result<KraftOrderView>> GetAsync(ApplicationUserId customerId, KraftOrderId orderId, CancellationToken cancellationToken)
    {
        var order = await orders.FindForReadAsync(orderId, cancellationToken);
        if (order is null || order.CustomerUserId != customerId.Value)
        {
            return OrderNotFound;
        }

        var payment = order.CurrentPaymentId is { } paymentId ? await payments.FindAsync(paymentId, cancellationToken) : null;

        return KraftOrderView.From(order, payment, time.GetUtcNow());
    }

    public async Task<Result<IReadOnlyList<KraftOrderView>>> ListMineAsync(ApplicationUserId customerId, CancellationToken cancellationToken)
    {
        var list = await orders.ListForCustomerAsync(customerId.Value, 100, cancellationToken);
        var now = time.GetUtcNow();

        return Result<IReadOnlyList<KraftOrderView>>.Success([.. list.Select(x => KraftOrderView.From(x, null, now))]);
    }

    /// <summary>KES <see cref="PlatformFeeOptions.FoodOrderFee" /> per Kraft order; none for waitlisted phones.</summary>
    private async Task<(decimal Fee, bool Waived)> ServiceFeeAsync(Guid eventId, string phone, CancellationToken cancellationToken)
    {
        var fees = feeOptions.Value;
        if (fees.FoodOrderFee == 0)
        {
            return (0m, false);
        }

        return fees.WaiveForWaitlist && await waitlist.IsOnWaitlistAsync(eventId, phone, cancellationToken)
            ? (0m, true)
            : (fees.FoodOrderFee, false);
    }

    private static Error OrderNotFound => CommerceErrors.NotFound("OrderNotFound", "The order was not found.");

    private async Task<Result<IReadOnlyList<PricedLine>>> PriceAsync(
        CheckoutRequest request,
        Guid eventId,
        CommerceOptions settings,
        CancellationToken cancellationToken)
    {
        var requested = (request.Items ?? []).ToList();
        var errors = new List<string>();

        if (requested.Count == 0)
        {
            return CommerceErrors.Invalid("CheckoutInvalid", ["Items: Choose at least one item."]);
        }

        if (requested.Exists(x => x.Quantity is < 1 or > 50))
        {
            errors.Add("Items: Each quantity must be between 1 and 50.");
        }

        var portions = requested.Sum(x => Math.Max(0, x.Quantity));
        if (portions > settings.MaxPortionsPerOrder)
        {
            errors.Add($"Items: At most {settings.MaxPortionsPerOrder} portions per checkout.");
        }

        if (errors.Count > 0)
        {
            return CommerceErrors.Invalid("CheckoutInvalid", errors);
        }

        // One line per item and add-on choice; the same item with other add-ons is its own line.
        var merged = requested
            .GroupBy(x => (x.MenuItemId, Key: string.Join(',', (x.AddOnIds ?? []).Distinct().Order())))
            .Select(x => (x.Key.MenuItemId, Quantity: x.Sum(y => y.Quantity), AddOnIds: (IReadOnlyCollection<Guid>)[.. (x.First().AddOnIds ?? []).Distinct()]))
            .ToList();
        var items = (await menu.ListByIdsAsync([.. merged.Select(x => x.MenuItemId)], cancellationToken)).ToDictionary(x => x.Id.Value);
        var sellers = (await krafts.ListByIdsForReadAsync([.. items.Values.Select(x => x.KraftId).Distinct()], cancellationToken)).ToDictionary(x => x.Id.Value);
        var selling = await tills.SellingAsync(eventId, sellers.Values, cancellationToken);

        var priced = new List<PricedLine>();
        var unavailable = new List<string>();

        foreach (var (menuItemId, quantity, addOnIds) in merged)
        {
            if (!items.TryGetValue(menuItemId, out var item) || item.EventId != eventId ||
                !sellers.TryGetValue(item.KraftId, out var kraft) || kraft.AtEvent(eventId) is null)
            {
                errors.Add($"Items: Item {menuItemId} is not on this event's menu.");
                continue;
            }

            if (!item.IsOrderable || !selling.Contains(item.KraftId) ||
                (request.Kind == KraftOrderKind.Live ? !item.SoldLive : !item.Reservable))
            {
                unavailable.Add($"{kraft.Name}: {item.Name}: Not available for {(request.Kind == KraftOrderKind.Live ? "live orders" : "reservation")} now.");
                continue;
            }

            var addOns = item.ChooseAddOns(addOnIds);
            if (addOns.IsFailure)
            {
                errors.AddRange(addOns.Error!.SubErrors ?? []);
                continue;
            }

            priced.Add(new PricedLine(kraft, item, quantity, addOns.Value!));
        }

        if (errors.Count > 0)
        {
            return CommerceErrors.Invalid("CheckoutInvalid", errors);
        }

        if (unavailable.Count > 0)
        {
            return new Error
            {
                Key = "Commerce.ItemUnavailable",
                Description = "Some of the items you chose cannot be ordered right now.",
                StatusCode = HttpStatusCode.Conflict,
                SubErrors = unavailable
            };
        }

        if (!request.AgeConfirmed && priced.Exists(x => x.Item.AgeRestricted))
        {
            return CommerceErrors.Invalid("AgeConfirmationRequired", ["AgeConfirmed: Confirm you are 18 or older to order these drinks."]);
        }

        return priced;
    }

    private async Task<Result<CheckoutView>> GroupViewAsync(IReadOnlyList<KraftOrder> group, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var views = new List<KraftOrderView>();

        foreach (var order in group.OrderBy(x => x.KraftName, StringComparer.OrdinalIgnoreCase))
        {
            var payment = order.CurrentPaymentId is { } paymentId && order.Status == KraftOrderStatus.Ordered
                ? await payments.FindAsync(paymentId, cancellationToken)
                : null;
            views.Add(KraftOrderView.From(order, payment, now));
        }

        var waiting = group.Where(x => x.Status == KraftOrderStatus.Ordered && x.HoldExpiresAt > now)
            .OrderBy(x => x.KraftName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CheckoutView(
            group[0].CheckoutGroupId,
            group[0].EventId,
            group[0].Kind.ToString(),
            group.Sum(x => x.Total),
            waiting.Count,
            waiting.FirstOrDefault()?.Id.Value,
            views);
    }

    /// <summary>
    ///     O-04: live orders need a usable ticket for the event date that is on now; reservations (made before the
    ///     event) need one for any date of the event.
    /// </summary>
    private async Task<Result> CheckTicketAsync(
        KraftOrderKind kind,
        string phone,
        Guid eventId,
        DateTimeOffset now,
        CommerceOptions settings,
        CancellationToken cancellationToken)
    {
        if (kind != KraftOrderKind.Live)
        {
            return await ticketHolders.HoldsTicketAsync(phone, eventId, cancellationToken)
                ? Result.Success
                : CommerceErrors.Forbidden("TicketRequired", "Food and drink orders are for ticket holders. Get your ticket first (fully paid).");
        }

        var sale = await events.GetSaleSnapshotAsync(eventId, cancellationToken);
        if (sale is null || OrderingWindows.LiveDateAt(sale.Dates, now, settings) is not { } dateId)
        {
            return CommerceErrors.Conflict("LiveOrderingClosed", "Live ordering is open on the day of the event only.");
        }

        return await ticketHolders.HoldsTicketForDateAsync(phone, dateId, cancellationToken)
            ? Result.Success
            : CommerceErrors.Forbidden("TicketRequired", "Live orders are for people with a ticket for today. Get your ticket first (fully paid).");
    }

    private async Task<Result<(string Phone, string Name)>> CustomerAsync(ApplicationUserId customerId, CancellationToken cancellationToken)
    {
        var profile = await customers.FindAsync(customerId, cancellationToken);

        if (profile is null)
        {
            return CommerceErrors.Forbidden("CustomerNotFound", "Sign in on this site to order.");
        }

        return profile.Phone is null || !KenyanMobile.TryCreate(profile.Phone, out var phone)
            ? CommerceErrors.Conflict("PhoneRequired", "Orders are tied to a Kenyan mobile number. Sign in with your phone.")
            : (phone!.Value, profile.DisplayName);
    }

    private static Result<string> ResolvePayer(string? requested, string fallback)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return fallback;
        }

        return KenyanMobile.TryCreate(requested, out var phone)
            ? phone!.Value
            : CommerceErrors.Invalid("PayerPhoneInvalid", ["PayerPhone: Enter a Kenyan M-Pesa number, e.g. 0712 345 678."]);
    }

    private async Task<string> NewReferenceAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxReferenceAttempts; attempt++)
        {
            var reference = ShortCode.New(KraftOrder.ReferenceLength);
            if (!await orders.ReferenceExistsAsync(reference, cancellationToken))
            {
                return reference;
            }
        }

        throw new InvalidOperationException("Could not generate a unique order reference.");
    }

    private sealed record PricedLine(Kraft Kraft, MenuItem Item, int Quantity, IReadOnlyList<MenuAddOn> AddOns);
}
