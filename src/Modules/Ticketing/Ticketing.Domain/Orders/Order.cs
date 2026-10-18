using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Ticketing.Domain.Common;

namespace Ticketing.Domain.Orders;

/// <summary>
///     A customer's ticket order for one event (UF-003). Capacity is held for <see cref="HoldExpiresAt" /> while
///     payment is pending (Spec 08 §8.1); the holds themselves live in the capacity ledger, keyed by order id.
///     Tickets are issued when payment is confirmed.
/// </summary>
public sealed class Order : AggregateRoot<OrderId>
{
    public const int ReferenceLength = 8;
    public const int IdempotencyKeyMaxLength = 64;
    public const int NameMaxLength = 120;
    public const int FailureMaxLength = 200;

    private readonly List<OrderLine> _lines = [];

    private Order(OrderId id)
        : base(id)
    {
    }

    public Guid EventId { get; private set; }

    /// <summary>Short human reference ("7K3M9Q2B"): shown to the customer and sent to M-Pesa as the account reference.</summary>
    public string Reference { get; private set; } = string.Empty;

    public Guid CustomerUserId { get; private set; }

    /// <summary>E.164 phone of the account that bought: tickets are issued to it.</summary>
    public string CustomerPhone { get; private set; } = string.Empty;

    public string CustomerName { get; private set; } = string.Empty;

    /// <summary>E.164 phone that receives the M-Pesa prompt (may differ from the customer's).</summary>
    public string PayerPhone { get; private set; } = string.Empty;

    public string IdempotencyKey { get; private set; } = string.Empty;

    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    /// <summary>What the customer pays: <see cref="Subtotal" /> plus <see cref="PlatformFee" />.</summary>
    public decimal Total { get; private set; }

    /// <summary>The tickets alone (sum of the lines).</summary>
    public decimal Subtotal { get; private set; }

    /// <summary>Customer-paid platform fee fixed when the order was placed (0 when waived).</summary>
    public decimal PlatformFee { get; private set; }

    /// <summary>The fee was waived because the customer's phone is on the event's waitlist.</summary>
    public bool FeeWaived { get; private set; }

    public int Admissions { get; private set; }

    public OrderStatus Status { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public DateTimeOffset HoldExpiresAt { get; private set; }

    public Guid? CurrentPaymentId { get; private set; }

    public int PaymentAttempts { get; private set; }

    public string? LastPaymentFailure { get; private set; }

    public Guid? PaidByPaymentId { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? ExpiredAt { get; private set; }

    public string? AttentionReason { get; private set; }

    public PaymentPlan PaymentPlan { get; private set; }

    /// <summary>What the first payment covers: the whole total, or the Lipa Mdogo Mdogo deposit.</summary>
    public decimal DepositAmount { get; private set; }

    public decimal AmountPaid { get; private set; }

    /// <summary>Lipa Mdogo Mdogo: when the balance must be paid; unpaid after that (plus grace) cancels the order.</summary>
    public DateTimeOffset? BalanceDueAt { get; private set; }

    public int BalanceRemindersSent { get; private set; }

    public DateTimeOffset? DepositPaidAt { get; private set; }

    public DateTimeOffset? AttentionResolvedAt { get; private set; }

    public Guid? AttentionResolvedBy { get; private set; }

    public string? AttentionResolution { get; private set; }

    /// <summary>Flagged for Finance and not resolved yet.</summary>
    public bool NeedsAction => AttentionReason is not null && AttentionResolvedAt is null;

    public DateTimeOffset? CancelledAt { get; private set; }

    public decimal BalanceDue => Math.Max(0, Total - AmountPaid);

    /// <summary>What the next M-Pesa prompt asks for.</summary>
    public decimal AmountDueNow => Status == OrderStatus.PartiallyPaid ? BalanceDue : DepositAmount;

    public bool IsHoldExpired(DateTimeOffset now) => Status == OrderStatus.AwaitingPayment && now >= HoldExpiresAt;

    public static Result<Order> Place(
        OrderId id,
        Guid eventId,
        string reference,
        OrderCustomer customer,
        string payerPhone,
        string idempotencyKey,
        IReadOnlyList<OrderLine> lines,
        int holdMinutes,
        DateTimeOffset now,
        LipaMdogoMdogoTerms? installments = null,
        OrderFee? fee = null)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(payerPhone);
        ArgumentOutOfRangeException.ThrowIfLessThan(holdMinutes, 1);

        if (lines.Count == 0)
        {
            return TicketingErrors.Invalid("OrderInvalid", ["Items: Choose at least one ticket."]);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > IdempotencyKeyMaxLength)
        {
            return TicketingErrors.Invalid("IdempotencyKeyInvalid", [$"Idempotency-Key: Send a unique key of at most {IdempotencyKeyMaxLength} characters."]);
        }

        var subtotal = lines.Sum(x => x.LineTotal);
        fee ??= OrderFee.None;

        if (fee.Amount < 0 || decimal.Round(fee.Amount, 0) != fee.Amount)
        {
            throw new ArgumentOutOfRangeException(nameof(fee), "The platform fee must be whole shillings.");
        }

        var total = subtotal + fee.Amount;

        if (subtotal <= 0 || decimal.Round(subtotal, 0) != subtotal)
        {
            // M-Pesa only moves whole shillings, and free orders have no payment to wait for.
            return TicketingErrors.Conflict("PriceNotPayable", "This selection cannot be paid with M-Pesa (free or not whole shillings).");
        }

        var deposit = total;
        if (installments is not null)
        {
            // Whole shillings: the deposit rounds up, the balance takes the rest.
            // The platform fee is paid in full with the deposit.
            deposit = Math.Ceiling(subtotal * installments.DepositPercent / 100m) + fee.Amount;
            if (deposit <= 0 || deposit >= total)
            {
                return TicketingErrors.Conflict("LipaMdogoMdogoUnavailable", "This order is too small to pay in installments.");
            }
        }

        var order = new Order(id)
        {
            EventId = eventId,
            Reference = reference,
            CustomerUserId = customer.UserId,
            CustomerPhone = customer.Phone,
            CustomerName = customer.Name.Length <= NameMaxLength ? customer.Name : customer.Name[..NameMaxLength],
            PayerPhone = payerPhone,
            IdempotencyKey = idempotencyKey,
            Total = total,
            Subtotal = subtotal,
            PlatformFee = fee.Amount,
            FeeWaived = fee.Waived,
            Admissions = lines.Sum(x => x.Admissions),
            Status = OrderStatus.AwaitingPayment,
            PlacedAt = now,
            HoldExpiresAt = now.AddMinutes(holdMinutes),
            PaymentPlan = installments is null ? PaymentPlan.Full : PaymentPlan.LipaMdogoMdogo,
            DepositAmount = deposit,
            BalanceDueAt = installments?.BalanceDueAt
        };

        order._lines.AddRange(lines);
        order.RaiseEvent(new OrderPlaced(id, eventId, order.Admissions, total));

        return order;
    }

    /// <summary>
    ///     Starts a (new) M-Pesa prompt: for the deposit or full amount while the hold is valid, or for the Lipa Mdogo
    ///     Mdogo balance until the order is cancelled.
    /// </summary>
    public Result StartPayment(Guid paymentId, int maxAttempts, DateTimeOffset now)
    {
        if (Status == OrderStatus.PartiallyPaid)
        {
            return StartBalancePayment(paymentId, maxAttempts);
        }

        if (Status != OrderStatus.AwaitingPayment)
        {
            return TicketingErrors.Conflict("OrderNotAwaitingPayment", "This order is not waiting for payment.");
        }

        if (IsHoldExpired(now))
        {
            return TicketingErrors.Conflict("HoldExpired", "Your seats were released. Start a new order.");
        }

        if (PaymentAttempts >= maxAttempts)
        {
            return TicketingErrors.Conflict("TooManyPaymentAttempts", "Too many payment attempts for this order. Start a new order.");
        }

        var @event = new OrderPaymentStarted(Id, paymentId, DepositAmount);

        CurrentPaymentId = paymentId;
        PaymentAttempts++;
        LastPaymentFailure = null;

        RaiseEvent(@event);

        return Result.Success;
    }

    /// <summary>The current prompt failed; the customer may try again while the hold (or the balance window) lasts.</summary>
    public void RecordPaymentFailed(Guid paymentId, string reason)
    {
        if (Status is not (OrderStatus.AwaitingPayment or OrderStatus.PartiallyPaid) || CurrentPaymentId != paymentId)
        {
            return;
        }

        var trimmed = reason.Length <= FailureMaxLength ? reason : reason[..FailureMaxLength];
        var @event = new OrderPaymentFailed(Id, paymentId, trimmed);

        LastPaymentFailure = trimmed;

        RaiseEvent(@event);
    }

    /// <summary>
    ///     Confirmed money for the order. From <c>AwaitingPayment</c>, or from <c>Expired</c> when the seats could be
    ///     taken again (the caller checks capacity first).
    /// </summary>
    public Result MarkPaid(Guid paymentId, DateTimeOffset now)
    {
        if (Status is not (OrderStatus.AwaitingPayment or OrderStatus.Expired))
        {
            return TicketingErrors.Conflict("OrderNotPayable", "This order can no longer be paid.");
        }

        if (PaymentPlan == PaymentPlan.LipaMdogoMdogo)
        {
            return MarkDepositPaid(paymentId, now);
        }

        var @event = new OrderPaid(Id, paymentId, Status == OrderStatus.Expired);

        AmountPaid = Total;
        Status = OrderStatus.Paid;
        PaidByPaymentId = paymentId;
        CurrentPaymentId = paymentId;
        PaidAt = now;
        LastPaymentFailure = null;

        RaiseEvent(@event);

        return Result.Success;
    }

    /// <summary>The Lipa Mdogo Mdogo balance arrived: the order is fully paid (the caller activates its tickets).</summary>
    public Result MarkBalancePaid(Guid paymentId, DateTimeOffset now)
    {
        if (Status != OrderStatus.PartiallyPaid)
        {
            return TicketingErrors.Conflict("OrderNotPayable", "This order has no balance to pay.");
        }

        var @event = new OrderPaid(Id, paymentId, false);

        Status = OrderStatus.Paid;
        AmountPaid = Total;
        PaidByPaymentId = paymentId;
        CurrentPaymentId = paymentId;
        PaidAt = now;
        LastPaymentFailure = null;

        RaiseEvent(@event);

        return Result.Success;
    }

    /// <summary>A balance reminder went out (the job sends at most one per reminder slot).</summary>
    public void RecordBalanceReminder() => BalanceRemindersSent++;

    /// <summary>
    ///     The balance was not paid in time: the order is cancelled and the caller voids its tickets and returns the
    ///     seats. The deposit is not refunded automatically (refunds are dormant, D-01); Finance decides.
    /// </summary>
    public bool CancelForUnpaidBalance(DateTimeOffset now)
    {
        if (Status != OrderStatus.PartiallyPaid)
        {
            return false;
        }

        var @event = new OrderCancelled(Id, "Balance not paid by the due date");

        Status = OrderStatus.Cancelled;
        CancelledAt = now;

        RaiseEvent(@event);

        return true;
    }

    /// <summary>
    ///     Finance records how a flagged payment was handled (refunded, seats found, kept as credit...). The order's
    ///     status does not change; the note is the record.
    /// </summary>
    public Result ResolveAttention(string? resolution, Guid resolvedBy, DateTimeOffset now)
    {
        if (!NeedsAction)
        {
            return TicketingErrors.Conflict("NothingToResolve", "This order has no open issue.");
        }

        var trimmed = resolution?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > FailureMaxLength * 2)
        {
            return TicketingErrors.Invalid("ResolutionInvalid", [$"Resolution: Describe what was done in 1-{FailureMaxLength * 2} characters."]);
        }

        var @event = new OrderAttentionResolved(Id);

        AttentionResolution = trimmed;
        AttentionResolvedBy = resolvedBy;
        AttentionResolvedAt = now;

        RaiseEvent(@event);

        return Result.Success;
    }

    /// <summary>The hold ran out without payment; the caller releases the held capacity in the same transaction.</summary>
    public bool Expire(DateTimeOffset now)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            return false;
        }

        var @event = new OrderExpired(Id);

        Status = OrderStatus.Expired;
        ExpiredAt = now;

        RaiseEvent(@event);

        return true;
    }

    private Result MarkDepositPaid(Guid paymentId, DateTimeOffset now)
    {
        var @event = new OrderDepositPaid(Id, paymentId, DepositAmount, Status == OrderStatus.Expired);

        Status = OrderStatus.PartiallyPaid;
        AmountPaid = DepositAmount;
        DepositPaidAt = now;
        CurrentPaymentId = paymentId;
        PaymentAttempts = 0;
        LastPaymentFailure = null;

        RaiseEvent(@event);

        return Result.Success;
    }

    private Result StartBalancePayment(Guid paymentId, int maxAttempts)
    {
        if (PaymentAttempts >= maxAttempts)
        {
            return TicketingErrors.Conflict("TooManyPaymentAttempts", "Too many payment attempts. Contact support to pay the balance.");
        }

        var @event = new OrderPaymentStarted(Id, paymentId, BalanceDue);

        CurrentPaymentId = paymentId;
        PaymentAttempts++;
        LastPaymentFailure = null;

        RaiseEvent(@event);

        return Result.Success;
    }

    /// <summary>
    ///     Money the order cannot use (late payment after the seats were sold, or a second payment). An expired order
    ///     moves to <c>NeedsAttention</c>; a paid order keeps its tickets and only records the event.
    /// </summary>
    public void FlagUnusablePayment(Guid paymentId, string reason)
    {
        var trimmed = reason.Length <= FailureMaxLength ? reason : reason[..FailureMaxLength];
        var @event = new OrderNeedsAttention(Id, paymentId, trimmed);

        if (Status is OrderStatus.Expired or OrderStatus.AwaitingPayment)
        {
            Status = OrderStatus.NeedsAttention;
        }

        AttentionReason = trimmed;
        AttentionResolvedAt = null;
        AttentionResolvedBy = null;
        AttentionResolution = null;

        RaiseEvent(@event);
    }
}

/// <summary>The platform fee on an order: the amount charged, and whether it was waived (waitlist).</summary>
public sealed record OrderFee(decimal Amount, bool Waived)
{
    public static readonly OrderFee None = new(0m, false);
}

/// <param name="Phone">E.164.</param>
public sealed record OrderCustomer(Guid UserId, string Phone, string Name);

public enum PaymentPlan
{
    Full = 1,

    /// <summary>Deposit now, balance later (UF-005); tickets have no QR until the balance is paid (BR-010).</summary>
    LipaMdogoMdogo = 2
}

/// <param name="DepositPercent">Share of the total paid up front (50 for Event 001, O-03: whole order).</param>
public sealed record LipaMdogoMdogoTerms(int DepositPercent, DateTimeOffset BalanceDueAt);
