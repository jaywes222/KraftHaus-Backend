using Common.Domain.Currency;

namespace Events.Domain.Model;

/// <summary>Input for adding or changing an event date.</summary>
public sealed record DateSpec(
    string? Label,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset? DoorsOpenAt);

/// <summary>Input for adding or changing a ticket tier.</summary>
public sealed record TierSpec(
    string? Name,
    string? Description,
    int Capacity);

/// <summary>Input for adding or changing a sale phase.</summary>
public sealed record PhaseSpec(
    string? Name,
    Money Price,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Allocation,
    bool EarlyAccess = false);

/// <summary>Input for adding or changing a Couple / Gang group offer. The kind is fixed once created.</summary>
public sealed record OfferSpec(
    GroupOfferKind Kind,
    string? Name,
    int Admits,
    Money Price,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Allocation);
