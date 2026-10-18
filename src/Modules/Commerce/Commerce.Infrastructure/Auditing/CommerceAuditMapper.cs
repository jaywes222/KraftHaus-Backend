using System.Globalization;
using Commerce.Domain.Krafts;
using Commerce.Domain.Menus;
using Commerce.Domain.Orders;
using Commerce.Domain.Settlements;
using Common.Application.Auditing;
using Common.Domain.Aggregates;
using Common.Domain.Events;

namespace Commerce.Infrastructure.Auditing;

/// <summary>
///     Commerce domain events → audit actions (<c>commerce.kraft.*</c>, <c>commerce.menu_item.*</c>,
///     <c>commerce.order.*</c>). Metadata never carries phone numbers or names.
/// </summary>
public static class CommerceAuditMapper
{
    public const string Module = "commerce";

    public static (string Action, AuditTarget Target, IReadOnlyDictionary<string, string> Metadata) Map(
        DomainEvent domainEvent,
        IAggregateRoot aggregate)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(aggregate);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        var (action, targetType) = domainEvent switch
        {
            KraftCreated created => (With("commerce.kraft.created", "discipline", created.Discipline.ToString()), "kraft"),
            KraftProfileUpdated => ("commerce.kraft.profile_updated", "kraft"),
            KraftBrandAdded added => (With("commerce.kraft.brand_added", "brandId", Id(added.BrandId.Value)), "kraft"),
            KraftBrandUpdated updated => (With("commerce.kraft.brand_updated", "brandId", Id(updated.BrandId.Value)), "kraft"),
            KraftBrandRemoved removed => (With("commerce.kraft.brand_removed", "brandId", Id(removed.BrandId.Value)), "kraft"),
            KrafterAdded added => (With("commerce.kraft.krafter_added", "krafterId", Id(added.KrafterId.Value)), "kraft"),
            KrafterUpdated updated => (With("commerce.kraft.krafter_updated", "krafterId", Id(updated.KrafterId.Value)), "kraft"),
            KrafterRemoved removed => (With("commerce.kraft.krafter_removed", "krafterId", Id(removed.KrafterId.Value)), "kraft"),
            KraftJoinedEvent joined => (Terms("commerce.kraft.joined_event", joined.EventId, joined.CommissionPercent), "kraft"),
            KraftEventTermsChanged changed => (Terms("commerce.kraft.event_terms_changed", changed.EventId, changed.CommissionPercent), "kraft"),
            KraftWithdrawnFromEvent withdrawn => (With("commerce.kraft.withdrawn_from_event", "eventId", Id(withdrawn.EventId)), "kraft"),
            KraftArchived => ("commerce.kraft.archived", "kraft"),
            MenuItemAdded added => (MenuAdded(added), "menu_item"),
            MenuItemUpdated updated => (Price("commerce.menu_item.updated", updated.OldPrice, updated.NewPrice), "menu_item"),
            MenuItemAvailabilityChanged availability => (With("commerce.menu_item.availability_changed", "available", availability.Available ? "yes" : "no"), "menu_item"),
            MenuItemRetired => ("commerce.menu_item.retired", "menu_item"),
            MenuItemImageChanged => ("commerce.menu_item.image_changed", "menu_item"),
            KraftSettlementRecorded recorded => (Settled(recorded), "settlement"),
            KraftOrderPlaced placed => (Placed(placed), "order"),
            KraftOrderPaymentStarted started => (With("commerce.order.payment_started", "paymentId", Id(started.PaymentId)), "order"),
            KraftOrderPaymentFailed failed => (Failed(failed), "order"),
            KraftOrderPaid paid => (Paid(paid), "order"),
            KraftOrderExpired => ("commerce.order.expired", "order"),
            KraftOrderNeedsAttention attention => (Attention(attention), "order"),
            KraftOrderPreparing => ("commerce.order.preparing", "order"),
            KraftOrderReady => ("commerce.order.ready", "order"),
            KraftOrderCollected => ("commerce.order.collected", "order"),
            KraftOrderLineUnavailable unavailable => (Unavailable(unavailable), "order"),
            KraftOrderUncollected => ("commerce.order.uncollected", "order"),
            KraftOrderAttentionResolved => ("commerce.order.attention_resolved", "order"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(domainEvent),
                domainEvent.GetType().Name,
                "Every Commerce domain event needs a stable audit action.")
        };

        return (action, new AuditTarget(targetType, aggregate.Id.Value.ToString("D", CultureInfo.InvariantCulture)), metadata);

        string Terms(string name, Guid eventId, decimal commission)
        {
            metadata["eventId"] = Id(eventId);
            metadata["commissionPercent"] = commission.ToString(CultureInfo.InvariantCulture);
            return name;
        }

        string Settled(KraftSettlementRecorded recorded)
        {
            metadata["kraftId"] = Id(recorded.KraftId);
            metadata["eventId"] = Id(recorded.EventId);
            metadata["direction"] = recorded.Direction.ToString();
            metadata["amount"] = recorded.Amount.ToString(CultureInfo.InvariantCulture);
            return "commerce.settlement.recorded";
        }

        string MenuAdded(MenuItemAdded added)
        {
            metadata["kraftId"] = Id(added.KraftId);
            metadata["eventId"] = Id(added.EventId);
            metadata["price"] = added.Price.ToString(CultureInfo.InvariantCulture);
            return "commerce.menu_item.added";
        }

        string Price(string name, decimal oldPrice, decimal newPrice)
        {
            metadata["oldPrice"] = oldPrice.ToString(CultureInfo.InvariantCulture);
            metadata["newPrice"] = newPrice.ToString(CultureInfo.InvariantCulture);
            return name;
        }

        string Placed(KraftOrderPlaced placed)
        {
            metadata["kraftId"] = Id(placed.KraftId);
            metadata["eventId"] = Id(placed.EventId);
            metadata["kind"] = placed.Kind.ToString();
            metadata["total"] = placed.Total.ToString(CultureInfo.InvariantCulture);
            metadata["portions"] = placed.Portions.ToString(CultureInfo.InvariantCulture);
            return "commerce.order.placed";
        }

        string Failed(KraftOrderPaymentFailed failed)
        {
            metadata["paymentId"] = Id(failed.PaymentId);
            metadata["reason"] = failed.Reason;
            return "commerce.order.payment_failed";
        }

        string Paid(KraftOrderPaid paid)
        {
            metadata["paymentId"] = Id(paid.PaymentId);
            metadata["total"] = paid.Total.ToString(CultureInfo.InvariantCulture);
            metadata["platformFee"] = paid.PlatformFee.ToString(CultureInfo.InvariantCulture);
            metadata["late"] = paid.Late ? "yes" : "no";
            return "commerce.order.paid";
        }

        string Attention(KraftOrderNeedsAttention attention)
        {
            metadata["paymentId"] = Id(attention.PaymentId);
            metadata["reason"] = attention.Reason;
            return "commerce.order.needs_attention";
        }

        string Unavailable(KraftOrderLineUnavailable unavailable)
        {
            metadata["lineId"] = Id(unavailable.LineId.Value);
            metadata["amount"] = unavailable.Amount.ToString(CultureInfo.InvariantCulture);
            return "commerce.order.line_unavailable";
        }

        string With(string name, string key, string value)
        {
            metadata[key] = value;
            return name;
        }
    }

    private static string Id(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
}
