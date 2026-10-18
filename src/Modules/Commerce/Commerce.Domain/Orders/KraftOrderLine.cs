using Commerce.Domain.Common;
using Common.Domain.Entities;

namespace Commerce.Domain.Orders;

/// <summary>One item of an order, priced when the order was placed (later menu changes never touch it).</summary>
public sealed class KraftOrderLine : AuditableEntity<KraftOrderLineId>
{
    private KraftOrderLine(KraftOrderLineId id)
        : base(id)
    {
    }

    public Guid MenuItemId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int Quantity { get; private set; }

    /// <summary>Item price plus the chosen add-ons, per unit.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>The chosen add-ons as the kitchen reads them ("Extra avocado, No onions"), or null.</summary>
    public string? AddOns { get; private set; }

    public const int AddOnsMaxLength = 400;

    public bool AgeRestricted { get; private set; }

    /// <summary>Ran out after payment (O-05); its money is owed back.</summary>
    public bool Unavailable { get; private set; }

    public string? UnavailableNote { get; private set; }

    public DateTimeOffset? UnavailableAt { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity;

    public static KraftOrderLine Create(
        KraftOrderLineId id,
        Guid menuItemId,
        string name,
        int quantity,
        decimal unitPrice,
        bool ageRestricted,
        string? addOns = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(unitPrice);

        var extras = string.IsNullOrWhiteSpace(addOns) ? null : addOns.Trim();
        if (extras is { Length: > AddOnsMaxLength })
        {
            extras = extras[..AddOnsMaxLength];
        }

        return new KraftOrderLine(id)
        {
            MenuItemId = menuItemId,
            Name = name,
            Quantity = quantity,
            UnitPrice = unitPrice,
            AgeRestricted = ageRestricted,
            AddOns = extras
        };
    }

    internal void MarkUnavailable(string note, DateTimeOffset now)
    {
        Unavailable = true;
        UnavailableNote = note;
        UnavailableAt = now;
    }
}
