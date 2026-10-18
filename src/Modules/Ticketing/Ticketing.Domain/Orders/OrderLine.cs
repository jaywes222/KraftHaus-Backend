using Common.Domain.Entities;
using Ticketing.Domain.Common;

namespace Ticketing.Domain.Orders;

/// <summary>One priced line of an order, frozen at checkout (later price changes never touch it).</summary>
public sealed class OrderLine : AuditableEntity<OrderLineId>
{
    public const int NameMaxLength = 160;

    private OrderLine(OrderLineId id)
        : base(id)
    {
    }

    public OrderItemKind Kind { get; private set; }

    public Guid EventDateId { get; private set; }

    public Guid TierId { get; private set; }

    /// <summary>The sale phase the admission price came from (Admission lines).</summary>
    public Guid? PhaseId { get; private set; }

    /// <summary>The Couple / Gang offer (Bundle lines).</summary>
    public Guid? OfferId { get; private set; }

    /// <summary>"Regular · Early Bird", "VIP · Couple ticket".</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Admissions or bundles bought.</summary>
    public int Units { get; private set; }

    public int AdmissionsPerUnit { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int Admissions => Units * AdmissionsPerUnit;

    public decimal LineTotal => UnitPrice * Units;

    public static OrderLine Create(OrderLineId id, OrderLineSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentOutOfRangeException.ThrowIfLessThan(spec.Units, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(spec.AdmissionsPerUnit, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(spec.UnitPrice);

        return new OrderLine(id)
        {
            Kind = spec.Kind,
            EventDateId = spec.EventDateId,
            TierId = spec.TierId,
            PhaseId = spec.PhaseId,
            OfferId = spec.OfferId,
            Name = spec.Name.Length <= NameMaxLength ? spec.Name : spec.Name[..NameMaxLength],
            Units = spec.Units,
            AdmissionsPerUnit = spec.AdmissionsPerUnit,
            UnitPrice = spec.UnitPrice
        };
    }
}

public sealed record OrderLineSpec(
    OrderItemKind Kind,
    Guid EventDateId,
    Guid TierId,
    Guid? PhaseId,
    Guid? OfferId,
    string Name,
    int Units,
    int AdmissionsPerUnit,
    decimal UnitPrice);
