using Commerce.Domain.Common;
using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;

namespace Commerce.Domain.Orders;

public enum KraftOrderKind
{
    /// <summary>Ordered on the day from live stock (UF-008).</summary>
    Live = 1,

    /// <summary>Pre-ordered before the day from the reservation allocation, picked up at the stall (UF-007, UF-014).</summary>
    Reservation = 2
}

/// <summary>PRODUCT.md §5: ORDERED → PAID → PREPARING → READY → COLLECTED.</summary>
public enum KraftOrderStatus
{
    /// <summary>Portions held; waiting for the M-Pesa payment.</summary>
    Ordered = 1,

    Paid = 2,

    Preparing = 3,

    Ready = 4,

    Collected = 5,

    /// <summary>Not paid before the hold ran out; portions went back on sale.</summary>
    Expired = 6
}

/// <summary>
///     One Kraft's part of a customer's cart (D-03): paid with its own M-Pesa prompt into that Kraft's till, prepared on
///     that Kraft's kitchen board. A cart over several Krafts is several orders sharing a <see cref="CheckoutGroupId" />;
///     each succeeds or fails on its own (Spec 08 §7.3).
/// </summary>
public sealed class KraftOrder : AggregateRoot<KraftOrderId>
{
    public const int ReferenceLength = 6;
    public const int PickupCodeLength = 8;
    public const int NameMaxLength = 120;
    public const int IdempotencyKeyMaxLength = 64;
    public const int NoteMaxLength = 500;

    private readonly List<KraftOrderLine> _lines = [];

    private KraftOrder(KraftOrderId id)
        : base(id)
    {
    }

    public Guid CheckoutGroupId { get; private set; }

    public Guid KraftId { get; private set; }

    public string KraftName { get; private set; } = string.Empty;

    public Guid EventId { get; private set; }

    public KraftOrderKind Kind { get; private set; }

    /// <summary>Short reference shown on the kitchen board and the order-ready display ("7QK2MD").</summary>
    public string Reference { get; private set; } = string.Empty;

    /// <summary>Secret code behind the receipt QR, scanned (or typed) at the stall to hand the order over.</summary>
    public string PickupCode { get; private set; } = string.Empty;

    public Guid CustomerUserId { get; private set; }

    public string CustomerPhone { get; private set; } = string.Empty;

    public string CustomerName { get; private set; } = string.Empty;

    public string PayerPhone { get; private set; } = string.Empty;

    public string IdempotencyKey { get; private set; } = string.Empty;

    public IReadOnlyList<KraftOrderLine> Lines => _lines.AsReadOnly();

    /// <summary>What the customer pays: <see cref="Subtotal" /> plus <see cref="ServiceFee" />.</summary>
    public decimal Total { get; private set; }

    /// <summary>The food and drinks alone (sum of the lines). Commission is taken on this.</summary>
    public decimal Subtotal { get; private set; }

    /// <summary>Customer-paid platform fee on this Kraft order (0 when waived). Collected into the Kraft's till and owed to the platform.</summary>
    public decimal ServiceFee { get; private set; }

    /// <summary>The service fee was waived because the customer's phone is on the event's waitlist.</summary>
    public bool ServiceFeeWaived { get; private set; }

    /// <summary>The money goes to the platform's till (the Kraft is paid its share at settlement).</summary>
    public bool PaidIntoPlatformTill { get; private set; }

    /// <summary>The Kraft's commission at the event when the order was placed.</summary>
    public decimal CommissionPercent { get; private set; }

    /// <summary>What the platform is owed, fixed when the payment is confirmed (Spec 08 §7.4): commission on the subtotal plus the service fee.</summary>
    public decimal? PlatformFee { get; private set; }

    /// <summary>What the Kraft keeps, fixed when the payment is confirmed.</summary>
    public decimal? KraftRevenue { get; private set; }

    public KraftOrderStatus Status { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public DateTimeOffset HoldExpiresAt { get; private set; }

    public Guid? CurrentPaymentId { get; private set; }

    public int PaymentAttempts { get; private set; }

    public string? LastPaymentFailure { get; private set; }

    public string? MpesaReceipt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? PreparingAt { get; private set; }

    public DateTimeOffset? ReadyAt { get; private set; }

    public DateTimeOffset? CollectedAt { get; private set; }

    public Guid? CollectedBy { get; private set; }

    /// <summary>Ready and not collected for too long (flagged on the kitchen board).</summary>
    public DateTimeOffset? UncollectedFlaggedAt { get; private set; }

    /// <summary>Why Finance must look at this order (money with no portions, a line that ran out after payment).</summary>
    public string? AttentionReason { get; private set; }

    /// <summary>Money owed back to the customer for unavailable lines or unusable payments (refunds are manual, D-01).</summary>
    public decimal AmountToRefund { get; private set; }

    public DateTimeOffset? AttentionResolvedAt { get; private set; }

    public Guid? AttentionResolvedBy { get; private set; }

    public string? AttentionResolution { get; private set; }

    public bool NeedsAction => AttentionReason is not null && AttentionResolvedAt is null;

    public bool IsPaid => Status is KraftOrderStatus.Paid or KraftOrderStatus.Preparing or KraftOrderStatus.Ready or KraftOrderStatus.Collected;

    public static KraftOrder Place(
        KraftOrderId id,
        KraftOrderSpec spec,
        IReadOnlyList<KraftOrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            throw new ArgumentException("An order needs at least one line.", nameof(lines));
        }

        var order = new KraftOrder(id)
        {
            CheckoutGroupId = spec.CheckoutGroupId,
            KraftId = spec.KraftId,
            KraftName = spec.KraftName,
            EventId = spec.EventId,
            Kind = spec.Kind,
            Reference = spec.Reference,
            PickupCode = spec.PickupCode,
            CustomerUserId = spec.CustomerUserId,
            CustomerPhone = spec.CustomerPhone,
            CustomerName = spec.CustomerName.Length <= NameMaxLength ? spec.CustomerName : spec.CustomerName[..NameMaxLength],
            PayerPhone = spec.PayerPhone,
            IdempotencyKey = spec.IdempotencyKey,
            CommissionPercent = spec.CommissionPercent,
            ServiceFee = spec.ServiceFee,
            ServiceFeeWaived = spec.ServiceFeeWaived,
            PaidIntoPlatformTill = spec.PlatformTill,
            Status = KraftOrderStatus.Ordered,
            PlacedAt = spec.Now,
            HoldExpiresAt = spec.HoldExpiresAt
        };

        order._lines.AddRange(lines);
        if (spec.ServiceFee < 0 || decimal.Round(spec.ServiceFee, 0) != spec.ServiceFee)
        {
            throw new ArgumentException("The service fee must be whole shillings.", nameof(spec));
        }

        order.Subtotal = lines.Sum(x => x.LineTotal);
        order.Total = order.Subtotal + spec.ServiceFee;

        order.RaiseEvent(new KraftOrderPlaced(id, spec.KraftId, spec.EventId, spec.Kind, order.Total, lines.Sum(x => x.Quantity)));

        return order;
    }

    /// <summary>A new prompt for this order is about to be sent (the payer may change).</summary>
    public Result StartPayment(Guid paymentId, string payerPhone, int maxAttempts, DateTimeOffset now)
    {
        if (Status != KraftOrderStatus.Ordered)
        {
            return CommerceErrors.Conflict("OrderNotPayable", "This order is not waiting for payment.");
        }

        if (now >= HoldExpiresAt)
        {
            return CommerceErrors.Conflict("OrderExpired", "The hold on this order ran out. Order again.");
        }

        if (PaymentAttempts >= maxAttempts)
        {
            return CommerceErrors.Conflict("TooManyPaymentAttempts", "Too many payment attempts for this order. Order again.");
        }

        CurrentPaymentId = paymentId;
        PayerPhone = payerPhone;
        PaymentAttempts++;
        LastPaymentFailure = null;

        RaiseEvent(new KraftOrderPaymentStarted(Id, paymentId));

        return Result.Success;
    }

    public void PaymentFailed(Guid paymentId, string reason)
    {
        if (Status != KraftOrderStatus.Ordered || CurrentPaymentId != paymentId)
        {
            return;
        }

        LastPaymentFailure = reason.Length <= NoteMaxLength ? reason : reason[..NoteMaxLength];
        RaiseEvent(new KraftOrderPaymentFailed(Id, paymentId, LastPaymentFailure));
    }

    /// <summary>Money confirmed and portions secured: fixes the fee split (Spec 08 §7.4).</summary>
    public void MarkPaid(Guid paymentId, string? receipt, bool late, DateTimeOffset now)
    {
        if (Status is not (KraftOrderStatus.Ordered or KraftOrderStatus.Expired))
        {
            throw new InvalidOperationException($"Order {Id.Value} cannot be paid from {Status}.");
        }

        var fee = decimal.Round(Subtotal * CommissionPercent / 100m, 2, MidpointRounding.AwayFromZero) + ServiceFee;

        CurrentPaymentId = paymentId;
        MpesaReceipt = receipt;
        PlatformFee = fee;
        KraftRevenue = Total - fee;
        Status = KraftOrderStatus.Paid;
        PaidAt = now;
        LastPaymentFailure = null;

        RaiseEvent(new KraftOrderPaid(Id, paymentId, Total, fee, late));
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status != KraftOrderStatus.Ordered)
        {
            return;
        }

        Status = KraftOrderStatus.Expired;
        RaiseEvent(new KraftOrderExpired(Id, now));
    }

    /// <summary>Money arrived that cannot be used (portions gone, order already paid): Finance refunds by hand.</summary>
    public void FlagUnusablePayment(Guid paymentId, decimal amount, string reason)
    {
        AddAttention(reason, amount);
        RaiseEvent(new KraftOrderNeedsAttention(Id, paymentId, reason));
    }

    public Result Accept(DateTimeOffset now)
    {
        if (Status != KraftOrderStatus.Paid)
        {
            return CommerceErrors.Conflict("OrderNotNew", "Only a new paid order can be accepted.");
        }

        Status = KraftOrderStatus.Preparing;
        PreparingAt = now;
        RaiseEvent(new KraftOrderPreparing(Id));

        return Result.Success;
    }

    public Result MarkReady(DateTimeOffset now)
    {
        if (Status is not (KraftOrderStatus.Paid or KraftOrderStatus.Preparing))
        {
            return CommerceErrors.Conflict("OrderNotInKitchen", "Only a paid order that is not ready yet can be marked ready.");
        }

        if (_lines.TrueForAll(x => x.Unavailable))
        {
            return CommerceErrors.Conflict("NothingToHandOver", "Every line of this order is unavailable; there is nothing to hand over.");
        }

        PreparingAt ??= now;
        Status = KraftOrderStatus.Ready;
        ReadyAt = now;
        RaiseEvent(new KraftOrderReady(Id));

        return Result.Success;
    }

    public Result Collect(Guid staffUserId, DateTimeOffset now)
    {
        if (Status == KraftOrderStatus.Collected)
        {
            return CommerceErrors.Conflict("AlreadyCollected", $"This order was collected at {CollectedAt:HH:mm} UTC.");
        }

        if (Status != KraftOrderStatus.Ready)
        {
            return CommerceErrors.Conflict("OrderNotReady", "This order is not ready yet.");
        }

        Status = KraftOrderStatus.Collected;
        CollectedAt = now;
        CollectedBy = staffUserId;
        RaiseEvent(new KraftOrderCollected(Id));

        return Result.Success;
    }

    /// <summary>
    ///     O-05: a line ran out after payment. The rest of the order goes ahead; the line's money is owed back and the order
    ///     waits for Finance.
    /// </summary>
    public Result MarkLineUnavailable(KraftOrderLineId lineId, string? note, DateTimeOffset now)
    {
        if (Status is not (KraftOrderStatus.Paid or KraftOrderStatus.Preparing))
        {
            return CommerceErrors.Conflict("OrderNotInKitchen", "Lines can only be marked unavailable before the order is ready.");
        }

        var line = _lines.Find(x => x.Id == lineId);
        if (line is null)
        {
            return CommerceErrors.NotFound("OrderLineNotFound", "The order line was not found.");
        }

        if (line.Unavailable)
        {
            return CommerceErrors.Conflict("LineAlreadyUnavailable", "This line is already marked unavailable.");
        }

        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > NoteMaxLength)
        {
            return CommerceErrors.Invalid("LineUnavailableInvalid", [$"Note: Say what happened, in 1-{NoteMaxLength} characters."]);
        }

        line.MarkUnavailable(trimmed, now);
        AddAttention($"{line.Name} x{line.Quantity} unavailable after payment: {trimmed}", line.LineTotal);

        RaiseEvent(new KraftOrderLineUnavailable(Id, lineId, line.LineTotal));

        return Result.Success;
    }

    public bool FlagUncollected(DateTimeOffset now)
    {
        if (Status != KraftOrderStatus.Ready || UncollectedFlaggedAt is not null)
        {
            return false;
        }

        UncollectedFlaggedAt = now;
        RaiseEvent(new KraftOrderUncollected(Id));

        return true;
    }

    public Result ResolveAttention(string? resolution, Guid resolvedBy, DateTimeOffset now)
    {
        if (!NeedsAction)
        {
            return CommerceErrors.Conflict("NothingToResolve", "This order has no open issue.");
        }

        var trimmed = resolution?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > NoteMaxLength)
        {
            return CommerceErrors.Invalid("ResolveInvalid", [$"Resolution: Say what was done, in 1-{NoteMaxLength} characters."]);
        }

        AttentionResolution = trimmed;
        AttentionResolvedAt = now;
        AttentionResolvedBy = resolvedBy;
        RaiseEvent(new KraftOrderAttentionResolved(Id));

        return Result.Success;
    }

    private void AddAttention(string reason, decimal amount)
    {
        var combined = AttentionReason is null || AttentionResolvedAt is not null ? reason : $"{AttentionReason} | {reason}";

        AttentionReason = combined.Length <= NoteMaxLength ? combined : combined[..NoteMaxLength];
        AmountToRefund += amount;
        AttentionResolvedAt = null;
        AttentionResolvedBy = null;
        AttentionResolution = null;
    }
}

public sealed record KraftOrderSpec(
    Guid CheckoutGroupId,
    Guid KraftId,
    string KraftName,
    Guid EventId,
    KraftOrderKind Kind,
    string Reference,
    string PickupCode,
    Guid CustomerUserId,
    string CustomerPhone,
    string CustomerName,
    string PayerPhone,
    string IdempotencyKey,
    decimal CommissionPercent,
    DateTimeOffset Now,
    DateTimeOffset HoldExpiresAt,
    decimal ServiceFee = 0m,
    bool ServiceFeeWaived = false,
    bool PlatformTill = false);
