using Common.Domain.Currency;
using Common.Domain.Entities;
using Events.Domain.Common;

namespace Events.Domain.Model;

/// <summary>
///     A priced sales phase of a tier ("Early Bird", "Advance", "Gate"). Phases of one tier never overlap, so at
///     any moment a tier has at most one current price.
/// </summary>
public sealed class SalePhase : AuditableEntity<SalePhaseId>
{
    public const int NameMaxLength = 80;

    private SalePhase(SalePhaseId id)
        : base(id)
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Price per admission. Stored as amount + currency columns.</summary>
    public Money Price
    {
        get => Money.Of(PriceAmount, Currency.From(PriceCurrency).Value).Value;
        private set
        {
            PriceAmount = value.Amount;
            PriceCurrency = value.Currency.Code;
        }
    }

    public decimal PriceAmount { get; private set; }

    public string PriceCurrency { get; private set; } = Currency.KES.Code;

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>Optional cap on admissions sold at this price (e.g. "first 100 at Early Bird").</summary>
    public int? Allocation { get; private set; }

    /// <summary>
    ///     Early access (PRODUCT.md): during this phase only phone numbers on the event's waitlist may buy this tier.
    ///     Ticketing enforces it at checkout.
    /// </summary>
    public bool EarlyAccess { get; private set; }

    public SalesWindow Window => SalesWindow.FromStored(StartsAt, EndsAt);

    internal static SalePhase Create(
        SalePhaseId id,
        string name,
        Money price,
        SalesWindow window,
        int? allocation,
        bool earlyAccess) =>
        new(id)
        {
            Name = name,
            Price = price,
            StartsAt = window.StartsAt,
            EndsAt = window.EndsAt,
            Allocation = allocation,
            EarlyAccess = earlyAccess
        };

    internal void Update(string name, Money price, SalesWindow window, int? allocation, bool earlyAccess)
    {
        Name = name;
        Price = price;
        StartsAt = window.StartsAt;
        EndsAt = window.EndsAt;
        Allocation = allocation;
        EarlyAccess = earlyAccess;
    }
}
