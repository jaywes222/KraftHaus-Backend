namespace Common.Application.EventCatalog;

/// <summary>
///     Read-only view of the resolved tenant's events for other modules (Ticketing reads it for the waitlist and,
///     later, checkout). Implemented by the Events module; callers never touch the Events schema directly.
/// </summary>
public interface IEventCatalog
{
    /// <summary>The event with this public slug in the host tenant, or <c>null</c> when there is none.</summary>
    Task<EventSnapshot?> FindBySlugAsync(string slug, CancellationToken cancellationToken);

    /// <summary>
    ///     Everything checkout needs to price and hold an order: dates, tiers, phases, offers and policies. <c>null</c>
    ///     when the event does not exist.
    /// </summary>
    Task<EventSaleSnapshot?> GetSaleSnapshotAsync(Guid eventId, CancellationToken cancellationToken);
}

/// <summary>Lifecycle of an event as other modules see it (mirrors the Events module's status).</summary>
public enum EventLifecycle
{
    Draft = 1,
    Published = 2,
    Cancelled = 3,
    Completed = 4
}

/// <param name="FirstStartsAt">Start of the earliest scheduled (not cancelled) date; <c>null</c> when none.</param>
/// <param name="LastEndsAt">End of the latest scheduled (not cancelled) date; <c>null</c> when none.</param>
public sealed record EventSnapshot(
    Guid EventId,
    string Slug,
    string Title,
    EventLifecycle Status,
    DateTimeOffset? FirstStartsAt,
    DateTimeOffset? LastEndsAt);

/// <summary>An event as checkout sees it. Prices are KES amounts; windows are half-open UTC intervals.</summary>
public sealed record EventSaleSnapshot(
    Guid EventId,
    string Slug,
    string Title,
    string? VenueName,
    EventLifecycle Status,
    EventSalePolicies Policies,
    IReadOnlyList<SaleDate> Dates);

public sealed record EventSalePolicies(
    bool RefundsEnabled,
    bool TransfersEnabled,
    int MaxTransfersPerTicket,
    bool LipaMdogoMdogoEnabled,
    int HoldMinutes,
    int MaxTicketsPerOrder);

public sealed record SaleDate(
    Guid DateId,
    string? Label,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset? DoorsOpenAt,
    bool Cancelled,
    IReadOnlyList<SaleTier> Tiers);

public sealed record SaleTier(
    Guid TierId,
    string Name,
    int Capacity,
    bool Withdrawn,
    IReadOnlyList<SalePhaseInfo> Phases,
    IReadOnlyList<SaleOffer> Offers);

public sealed record SalePhaseInfo(
    Guid PhaseId,
    string Name,
    decimal Price,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Allocation,
    bool EarlyAccess)
{
    public bool IsOpenAt(DateTimeOffset now) => now >= StartsAt && now < EndsAt;
}

/// <param name="Admits">Admissions per bundle (Couple 2, Gang 3+).</param>
/// <param name="Allocation">Optional cap on bundles sold.</param>
public sealed record SaleOffer(
    Guid OfferId,
    string Name,
    int Admits,
    decimal Price,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Allocation,
    bool Withdrawn)
{
    public bool IsOpenAt(DateTimeOffset now) => !Withdrawn && now >= StartsAt && now < EndsAt;
}
