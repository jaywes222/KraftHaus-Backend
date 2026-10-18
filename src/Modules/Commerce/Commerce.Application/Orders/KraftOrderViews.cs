using Commerce.Domain.Orders;
using Common.Application.Payments;

namespace Commerce.Application.Orders;

/// <param name="AddOnIds">Add-ons chosen for these units (each unit gets the same ones).</param>
public sealed record CheckoutItemRequest(Guid MenuItemId, int Quantity, IReadOnlyList<Guid>? AddOnIds = null);

/// <param name="AgeConfirmed">The customer confirmed they are 18 or older (needed for age-restricted items).</param>
public sealed record CheckoutRequest(
    string? EventSlug,
    KraftOrderKind Kind,
    IReadOnlyList<CheckoutItemRequest>? Items,
    bool AgeConfirmed,
    string? PayerPhone);

public sealed record PayOrderRequest(string? PayerPhone);

public sealed record ResolveKraftOrderRequest(string? Resolution);

public sealed record LineUnavailableRequest(string? Note, bool SwitchOffItem);

public sealed record CollectByCodeRequest(string? Code);

public sealed record KraftOrderLineView(
    Guid Id,
    Guid MenuItemId,
    string Name,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    bool AgeRestricted,
    bool Unavailable,
    string? UnavailableNote,
    string? AddOns)
{
    public static KraftOrderLineView From(KraftOrderLine line) =>
        new(line.Id.Value, line.MenuItemId, line.Name, line.Quantity, line.UnitPrice, line.LineTotal, line.AgeRestricted,
            line.Unavailable, line.UnavailableNote, line.AddOns);
}

/// <summary>
///     One order as its customer sees it. The pickup code (the receipt QR) appears only once the order is paid;
///     Shows the customer's service fee (or that it was waived); never the Kraft's commission.
/// </summary>
public sealed record KraftOrderView(
    Guid Id,
    Guid CheckoutGroupId,
    Guid KraftId,
    string KraftName,
    Guid EventId,
    string Kind,
    string Reference,
    string Status,
    decimal Total,
    IReadOnlyList<KraftOrderLineView> Lines,
    DateTimeOffset PlacedAt,
    DateTimeOffset HoldExpiresAt,
    int HoldSecondsLeft,
    string PayerPhone,
    int PaymentAttempts,
    Guid? PaymentId,
    string? PaymentStatus,
    string? PaymentErrorKey,
    string? LastPaymentFailure,
    string? MpesaReceipt,
    string? PickupCode,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? CollectedAt,
    decimal AmountToRefund,
    decimal Subtotal,
    decimal ServiceFee,
    bool ServiceFeeWaived)
{
    public static KraftOrderView From(KraftOrder order, MobilePaymentView? payment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);

        var left = order.Status == KraftOrderStatus.Ordered ? (int)Math.Max(0, (order.HoldExpiresAt - now).TotalSeconds) : 0;

        return new KraftOrderView(
            order.Id.Value,
            order.CheckoutGroupId,
            order.KraftId,
            order.KraftName,
            order.EventId,
            order.Kind.ToString(),
            order.Reference,
            order.Status.ToString(),
            order.Total,
            [.. order.Lines.Select(KraftOrderLineView.From)],
            order.PlacedAt,
            order.HoldExpiresAt,
            left,
            Mask(order.PayerPhone),
            order.PaymentAttempts,
            order.CurrentPaymentId,
            payment?.Status.ToString(),
            payment?.ErrorKey,
            order.LastPaymentFailure,
            order.MpesaReceipt,
            order.IsPaid ? order.PickupCode : null,
            order.PaidAt,
            order.ReadyAt,
            order.CollectedAt,
            order.AmountToRefund,
            order.Subtotal,
            order.ServiceFee,
            order.ServiceFeeWaived);
    }

    private static string Mask(string phone) => phone.Length > 4 ? $"{phone[..4]}••••{phone[^3..]}" : phone;
}

/// <summary>A cart over several Krafts: one order (and one M-Pesa prompt) per Kraft (Spec 08 §7.3).</summary>
/// <param name="PromptsToExpect">How many M-Pesa prompts the customer will get (one per Kraft not paid yet).</param>
/// <param name="NextOrderToPay">The order to pay next (pay them one at a time); null when none is left.</param>
public sealed record CheckoutView(
    Guid CheckoutGroupId,
    Guid EventId,
    string Kind,
    decimal Total,
    int PromptsToExpect,
    Guid? NextOrderToPay,
    IReadOnlyList<KraftOrderView> Orders);

/// <summary>A card on a Kraft's kitchen board (UF-015). The customer's first name only; no phone, no fees.</summary>
public sealed record KitchenOrderView(
    Guid Id,
    string Reference,
    string Kind,
    string Status,
    string CustomerFirstName,
    IReadOnlyList<KraftOrderLineView> Lines,
    DateTimeOffset? PaidAt,
    DateTimeOffset? PreparingAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? CollectedAt,
    int MinutesSincePaid,
    bool Uncollected,
    bool NeedsAction)
{
    public static KitchenOrderView From(KraftOrder order, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);

        var name = order.CustomerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        return new KitchenOrderView(
            order.Id.Value,
            order.Reference,
            order.Kind.ToString(),
            order.Status.ToString(),
            name,
            [.. order.Lines.Select(KraftOrderLineView.From)],
            order.PaidAt,
            order.PreparingAt,
            order.ReadyAt,
            order.CollectedAt,
            order.PaidAt is { } paid ? (int)Math.Max(0, (now - paid).TotalMinutes) : 0,
            order.UncollectedFlaggedAt is not null && order.Status == KraftOrderStatus.Ready,
            order.NeedsAction);
    }
}

/// <summary>An order as Finance and the Kraft Owner see it, with the fee split (Spec 08 §7.4).</summary>
public sealed record KraftOrderAdminView(
    Guid Id,
    Guid CheckoutGroupId,
    Guid KraftId,
    string KraftName,
    Guid EventId,
    string Kind,
    string Reference,
    string Status,
    string CustomerPhoneLast3,
    decimal Total,
    decimal CommissionPercent,
    decimal? PlatformFee,
    decimal? KraftRevenue,
    Guid? PaymentId,
    string? MpesaReceipt,
    int PaymentAttempts,
    IReadOnlyList<KraftOrderLineView> Lines,
    DateTimeOffset PlacedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? CollectedAt,
    bool NeedsAction,
    string? AttentionReason,
    decimal AmountToRefund,
    string? AttentionResolution,
    DateTimeOffset? AttentionResolvedAt,
    decimal Subtotal,
    decimal ServiceFee,
    bool ServiceFeeWaived)
{
    public static KraftOrderAdminView From(KraftOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new KraftOrderAdminView(
            order.Id.Value,
            order.CheckoutGroupId,
            order.KraftId,
            order.KraftName,
            order.EventId,
            order.Kind.ToString(),
            order.Reference,
            order.Status.ToString(),
            order.CustomerPhone[^3..],
            order.Total,
            order.CommissionPercent,
            order.PlatformFee,
            order.KraftRevenue,
            order.CurrentPaymentId,
            order.MpesaReceipt,
            order.PaymentAttempts,
            [.. order.Lines.Select(KraftOrderLineView.From)],
            order.PlacedAt,
            order.PaidAt,
            order.CollectedAt,
            order.NeedsAction,
            order.AttentionReason,
            order.AmountToRefund,
            order.AttentionResolution,
            order.AttentionResolvedAt,
            order.Subtotal,
            order.ServiceFee,
            order.ServiceFeeWaived);
    }
}

/// <summary>Sales of a Kraft (or every Kraft) at an event, from paid orders.</summary>
public sealed record KraftSalesView(
    Guid KraftId,
    string KraftName,
    int PaidOrders,
    int Portions,
    decimal Gross,
    decimal PlatformFees,
    decimal KraftRevenue,
    decimal OwedBack,
    decimal ServiceFees);
