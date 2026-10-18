using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Checkout;
using Ticketing.Domain.Common;
using Ticketing.Domain.Orders;
using Ticketing.Domain.Tickets;

namespace Ticketing.Application.Orders;

/// <summary>Admin and Finance view of orders, sales counters and the waitlist for one event.</summary>
public sealed class OrderAdminService(
    IOrderRepository orders,
    ITicketRepository tickets,
    ICapacityLedger capacity,
    IWaitlistRepository waitlist,
    ITicketingUnitOfWork unitOfWork,
    TimeProvider time)
{
    public const int MaxPageSize = 200;

    public async Task<Result<IReadOnlyList<OrderAdminView>>> ListAsync(
        Guid eventId,
        OrderStatus? status,
        bool? needsAction,
        DateTimeOffset? placedBefore,
        int? limit,
        CancellationToken cancellationToken)
    {
        var list = await orders.ListForEventAsync(eventId, status, needsAction == true, placedBefore, Math.Clamp(limit ?? 50, 1, MaxPageSize), cancellationToken);
        var now = time.GetUtcNow();

        return Result<IReadOnlyList<OrderAdminView>>.Success([.. list.Select(x => OrderAdminView.From(x, now))]);
    }

    public async Task<Result<OrderAdminView>> GetAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        var order = await orders.FindForReadAsync(orderId, cancellationToken);
        return order is null
            ? TicketingErrors.NotFound("OrderNotFound", "The order was not found.")
            : OrderAdminView.From(order, time.GetUtcNow());
    }

    /// <summary>Finance records how a flagged order (late or extra payment) was handled.</summary>
    public async Task<Result<OrderAdminView>> ResolveAsync(
        OrderId orderId,
        ResolveOrderRequest request,
        ApplicationUserId resolvedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var order = await orders.FindAsync(orderId, cancellationToken);
        if (order is null)
        {
            return TicketingErrors.NotFound("OrderNotFound", "The order was not found.");
        }

        var resolved = order.ResolveAttention(request.Resolution, resolvedBy.Value, time.GetUtcNow());
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : OrderAdminView.From(order, time.GetUtcNow());
    }

    public async Task<Result<IReadOnlyList<AdminTicketView>>> TicketsAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        var list = await tickets.ListForOrderAsync(orderId, cancellationToken);
        return Result<IReadOnlyList<AdminTicketView>>.Success([.. list.Select(AdminTicketView.From)]);
    }

    /// <summary>
    ///     Voids a ticket (lost, fraud, refunded off-system). The seat is not put back on sale automatically: refunds
    ///     are dormant (D-01) and the sale counters stay a record of what was paid.
    /// </summary>
    public async Task<Result<AdminTicketView>> VoidTicketAsync(TicketId ticketId, VoidTicketRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 200)
        {
            return TicketingErrors.Invalid("VoidInvalid", ["Reason: Say why, in 1-200 characters."]);
        }

        var ticket = await tickets.FindAsync(ticketId, cancellationToken);
        if (ticket is null)
        {
            return TicketingErrors.NotFound("TicketNotFound", "The ticket was not found.");
        }

        if (ticket.Status == TicketStatus.CheckedIn)
        {
            return TicketingErrors.Conflict("TicketCheckedIn", "A ticket that was already used at the door cannot be voided.");
        }

        ticket.Void(reason);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : AdminTicketView.From(ticket);
    }

    public async Task<Result<SalesSummaryView>> SalesAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var buckets = await capacity.ListForEventAsync(eventId, cancellationToken);
        var waitlisted = await waitlist.CountAsync(eventId, cancellationToken);
        var totals = await orders.TotalsForEventAsync(eventId, cancellationToken);
        var counts = await tickets.CountByStatusForEventAsync(eventId, cancellationToken);

        int Count(TicketStatus status) => counts.TryGetValue(status, out var n) ? n : 0;

        return new SalesSummaryView(
            eventId,
            waitlisted,
            buckets,
            new TicketCountsView(Count(TicketStatus.Valid), Count(TicketStatus.CheckedIn), Count(TicketStatus.AwaitingBalance), Count(TicketStatus.Void)),
            totals.PaidOrders,
            totals.AmountCollected,
            totals.PlatformFees,
            totals.FeesWaived);
    }

    /// <summary>Every ticket of the event (bought tickets), filterable by date, status and a search.</summary>
    public async Task<Result<IReadOnlyList<AdminTicketView>>> TicketsForEventAsync(
        Guid eventId,
        Guid? eventDateId,
        TicketStatus? status,
        string? query,
        DateTimeOffset? issuedBefore,
        int? limit,
        CancellationToken cancellationToken)
    {
        var search = query?.Trim();
        if (search is { Length: > 60 })
        {
            return TicketingErrors.Invalid("SearchInvalid", ["Q: Search with at most 60 characters."]);
        }

        var list = await tickets.ListForEventAsync(
            eventId, eventDateId, status, search, issuedBefore, Math.Clamp(limit ?? 100, 1, MaxPageSize), cancellationToken);

        return Result<IReadOnlyList<AdminTicketView>>.Success([.. list.Select(AdminTicketView.From)]);
    }

    public async Task<Result<IReadOnlyList<WaitlistAdminView>>> WaitlistAsync(
        Guid eventId,
        DateTimeOffset? joinedAfter,
        int? limit,
        CancellationToken cancellationToken)
    {
        var list = await waitlist.ListForReadAsync(eventId, joinedAfter, Math.Clamp(limit ?? 100, 1, 1000), cancellationToken);

        return Result<IReadOnlyList<WaitlistAdminView>>.Success([
            .. list.Select(x => new WaitlistAdminView(
                x.Id.Value, x.Name, x.Phone, x.Age, x.Gender.ToString(), x.Location, x.Email, x.MarketingConsent,
                x.JoinedAt, x.ConfirmationSentAt is not null))
        ]);
    }
}

/// <param name="CustomerPhoneLast3">Only the last three digits in lists; the full number is on the tickets.</param>
public sealed record OrderAdminView(
    Guid Id,
    string Reference,
    string Status,
    string CustomerName,
    string CustomerPhoneLast3,
    decimal Total,
    int Admissions,
    DateTimeOffset PlacedAt,
    DateTimeOffset HoldExpiresAt,
    DateTimeOffset? PaidAt,
    int PaymentAttempts,
    Guid? CurrentPaymentId,
    string? LastPaymentFailure,
    string? AttentionReason,
    bool NeedsAction,
    string? AttentionResolution,
    DateTimeOffset? AttentionResolvedAt,
    string PaymentPlan,
    decimal AmountPaid,
    DateTimeOffset? BalanceDueAt,
    IReadOnlyList<OrderLineView> Lines,
    decimal Subtotal,
    decimal PlatformFee,
    bool FeeWaived)
{
    public static OrderAdminView From(Order order, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderAdminView(
            order.Id.Value,
            order.Reference,
            order.IsHoldExpired(now) ? nameof(OrderStatus.Expired) : order.Status.ToString(),
            order.CustomerName,
            order.CustomerPhone[^3..],
            order.Total,
            order.Admissions,
            order.PlacedAt,
            order.HoldExpiresAt,
            order.PaidAt,
            order.PaymentAttempts,
            order.CurrentPaymentId,
            order.LastPaymentFailure,
            order.AttentionReason,
            order.NeedsAction,
            order.AttentionResolution,
            order.AttentionResolvedAt,
            order.PaymentPlan.ToString(),
            order.AmountPaid,
            order.BalanceDueAt,
            [.. order.Lines.Select(x => new OrderLineView(x.Name, x.Kind.ToString(), x.Units, x.Admissions, x.UnitPrice, x.LineTotal))],
            order.Subtotal,
            order.PlatformFee,
            order.FeeWaived);
    }
}

/// <param name="PaidOrders">Paid and part-paid (Lipa Mdogo Mdogo) orders.</param>
/// <param name="AmountCollected">Money received on those orders, platform fees included.</param>
/// <param name="FeesWaived">Orders whose fee was waived because the buyer is on the waitlist.</param>
public sealed record SalesSummaryView(
    Guid EventId,
    int Waitlisted,
    IReadOnlyList<CapacityBucketView> Buckets,
    TicketCountsView Tickets,
    int PaidOrders,
    decimal AmountCollected,
    decimal PlatformFees,
    int FeesWaived);

public sealed record TicketCountsView(int Valid, int CheckedIn, int AwaitingBalance, int Void);

public sealed record WaitlistAdminView(
    Guid Id,
    string Name,
    string Phone,
    int Age,
    string Gender,
    string Location,
    string? Email,
    bool MarketingConsent,
    DateTimeOffset JoinedAt,
    bool ConfirmationSent);

public sealed record ResolveOrderRequest(string? Resolution);

public sealed record VoidTicketRequest(string? Reason);

/// <param name="HolderPhoneLast3">The full number is the holder's; staff see the last three digits.</param>
public sealed record AdminTicketView(
    Guid Id,
    Guid OrderId,
    string Code,
    string Status,
    Guid EventDateId,
    string? DateLabel,
    DateTimeOffset DateStartsAt,
    string TierName,
    string HolderName,
    string HolderPhoneLast3,
    string? AttendeeName,
    Guid? BundleId,
    int TransfersUsed,
    DateTimeOffset IssuedAt,
    DateTimeOffset? CheckedInAt,
    string? CheckInMethod)
{
    public static AdminTicketView From(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        return new AdminTicketView(
            ticket.Id.Value,
            ticket.OrderId.Value,
            ticket.Code,
            ticket.Status.ToString(),
            ticket.EventDateId,
            ticket.DateLabel,
            ticket.DateStartsAt,
            ticket.TierName,
            ticket.HolderName,
            ticket.HolderPhone[^3..],
            ticket.AttendeeName,
            ticket.BundleId,
            ticket.TransferCount,
            ticket.IssuedAt,
            ticket.CheckedInAt,
            ticket.CheckInMethod?.ToString());
    }
}
