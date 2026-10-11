using Common.Domain.Currency;
using Common.Domain.Entities;
using Events.Domain.Common;

namespace Events.Domain.Model;

/// <summary>
///     A bundle of admissions to one tier sold for one price: a Couple ticket (2) or a Gang Gang ticket (3+). Each
///     bundle sold consumes <see cref="Admits" /> places of the tier's capacity.
/// </summary>
public sealed class GroupOffer : AuditableEntity<GroupOfferId>
{
    public const int NameMaxLength = 80;
    public const int CoupleAdmits = 2;
    public const int MinGangAdmits = 3;
    public const int MaxGangAdmits = 20;

    private GroupOffer(GroupOfferId id)
        : base(id)
    {
    }

    public GroupOfferKind Kind { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Admissions per bundle.</summary>
    public int Admits { get; private set; }

    /// <summary>Price of the whole bundle.</summary>
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

    /// <summary>Optional cap on the number of bundles sold.</summary>
    public int? Allocation { get; private set; }

    public SaleItemStatus Status { get; private set; }

    public SalesWindow Window => SalesWindow.FromStored(StartsAt, EndsAt);

    internal static GroupOffer Create(
        GroupOfferId id,
        GroupOfferKind kind,
        string name,
        int admits,
        Money price,
        SalesWindow window,
        int? allocation) =>
        new(id)
        {
            Kind = kind,
            Name = name,
            Admits = admits,
            Price = price,
            StartsAt = window.StartsAt,
            EndsAt = window.EndsAt,
            Allocation = allocation,
            Status = SaleItemStatus.Active
        };

    internal void Update(string name, int admits, Money price, SalesWindow window, int? allocation)
    {
        Name = name;
        Admits = admits;
        Price = price;
        StartsAt = window.StartsAt;
        EndsAt = window.EndsAt;
        Allocation = allocation;
    }

    internal void Withdraw() => Status = SaleItemStatus.Withdrawn;
}
