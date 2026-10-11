using Common.Domain.Currency;
using Events.Domain.Model;
using Events.Domain.Venues;

namespace Events.Application;

// -------------------------------------------------------------------------------------------------------------
// Admin views: everything, including drafts, capacities and allocations.
// -------------------------------------------------------------------------------------------------------------

public sealed record EntranceView(Guid Id, string Name, string? Description);

public sealed record VenueView(
    Guid Id,
    string Name,
    string? AddressLine,
    string? City,
    Uri? MapUrl,
    string? Directions,
    int? Capacity,
    VenueStatus Status,
    IReadOnlyList<EntranceView> Entrances)
{
    public static VenueView From(Venue venue)
    {
        ArgumentNullException.ThrowIfNull(venue);

        return new VenueView(
            venue.Id.Value,
            venue.Details.Name,
            venue.Details.AddressLine,
            venue.Details.City,
            venue.Details.MapUrl,
            venue.Details.Directions,
            venue.Capacity,
            venue.Status,
            [.. venue.Entrances.Select(x => new EntranceView(x.Id.Value, x.Name, x.Description))]);
    }
}

public sealed record EventVenueView(
    Guid? SourceVenueId,
    string Name,
    string? AddressLine,
    string? City,
    Uri? MapUrl,
    string? Directions);

public sealed record PoliciesView(
    bool RefundsEnabled,
    bool TransfersEnabled,
    int MaxTransfersPerTicket,
    bool LipaMdogoMdogoEnabled,
    int HoldMinutes,
    int MaxTicketsPerOrder)
{
    public static PoliciesView From(EventPolicies policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        return new PoliciesView(
            policies.RefundsEnabled,
            policies.TransfersEnabled,
            policies.MaxTransfersPerTicket,
            policies.LipaMdogoMdogoEnabled,
            policies.HoldMinutes,
            policies.MaxTicketsPerOrder);
    }
}

public sealed record SalePhaseView(
    Guid Id,
    string Name,
    Money Price,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Allocation,
    bool IsCurrent);

public sealed record GroupOfferView(
    Guid Id,
    GroupOfferKind Kind,
    string Name,
    int Admits,
    Money Price,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Allocation,
    SaleItemStatus Status,
    bool IsOnSale);

public sealed record TicketTierView(
    Guid Id,
    string Name,
    string? Description,
    int Capacity,
    int Position,
    SaleItemStatus Status,
    IReadOnlyList<SalePhaseView> Phases,
    IReadOnlyList<GroupOfferView> Offers);

public sealed record EventDateView(
    Guid Id,
    string? Label,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset? DoorsOpenAt,
    EventDateStatus Status,
    IReadOnlyList<TicketTierView> Tiers);

/// <summary>The full admin view of one event, returned by every admin read and write.</summary>
public sealed record EventView(
    Guid Id,
    string Slug,
    string Title,
    string? Summary,
    string? Description,
    Uri? PosterUrl,
    EventStatus Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    DateTimeOffset? CompletedAt,
    EventVenueView? Venue,
    IReadOnlyList<EntranceView> Entrances,
    PoliciesView Policies,
    IReadOnlyList<EventDateView> Dates,
    IReadOnlyList<string> PublishProblems,
    long Version)
{
    public static EventView From(Event @event, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(@event);

        return new EventView(
            @event.Id.Value,
            @event.Slug,
            @event.Title,
            @event.Summary,
            @event.Description,
            @event.PosterUrl,
            @event.Status,
            @event.PublishedAt,
            @event.CancelledAt,
            @event.CancellationReason,
            @event.CompletedAt,
            @event.Venue is { } venue
                ? new EventVenueView(
                    venue.SourceVenueId?.Value,
                    venue.Details.Name,
                    venue.Details.AddressLine,
                    venue.Details.City,
                    venue.Details.MapUrl,
                    venue.Details.Directions)
                : null,
            [.. @event.Entrances.Select(x => new EntranceView(x.Id.Value, x.Name, x.Description))],
            PoliciesView.From(@event.Policies),
            [.. @event.Dates.OrderBy(x => x.StartsAt).Select(date => DateView(date, now))],
            @event.Status == EventStatus.Draft ? @event.PublishProblems(now) : [],
            @event.Version);
    }

    private static EventDateView DateView(EventDate date, DateTimeOffset now) =>
        new(
            date.Id.Value,
            date.Label,
            date.StartsAt,
            date.EndsAt,
            date.DoorsOpenAt,
            date.Status,
            [.. date.Tiers.OrderBy(x => x.Position).Select(tier => TierView(tier, now))]);

    private static TicketTierView TierView(TicketTier tier, DateTimeOffset now) =>
        new(
            tier.Id.Value,
            tier.Name,
            tier.Description,
            tier.Capacity,
            tier.Position,
            tier.Status,
            [
                .. tier.Phases
                    .OrderBy(x => x.StartsAt)
                    .Select(x => new SalePhaseView(
                        x.Id.Value,
                        x.Name,
                        x.Price,
                        x.StartsAt,
                        x.EndsAt,
                        x.Allocation,
                        x.Window.Contains(now)))
            ],
            [
                .. tier.Offers
                    .OrderBy(x => x.StartsAt)
                    .Select(x => new GroupOfferView(
                        x.Id.Value,
                        x.Kind,
                        x.Name,
                        x.Admits,
                        x.Price,
                        x.StartsAt,
                        x.EndsAt,
                        x.Allocation,
                        x.Status,
                        x.Status == SaleItemStatus.Active && tier.Status == SaleItemStatus.Active &&
                        x.Window.Contains(now)))
            ]);
}

/// <summary>One row of the admin event list.</summary>
public sealed record EventSummaryView(
    Guid Id,
    string Slug,
    string Title,
    EventStatus Status,
    string? VenueName,
    DateTimeOffset? FirstStartsAt,
    DateTimeOffset? LastEndsAt,
    int DateCount)
{
    public static EventSummaryView From(Event @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var scheduled = @event.Dates.Where(x => x.Status == EventDateStatus.Scheduled).ToList();

        return new EventSummaryView(
            @event.Id.Value,
            @event.Slug,
            @event.Title,
            @event.Status,
            @event.Venue?.Details.Name,
            scheduled.Count == 0 ? null : scheduled.Min(x => x.StartsAt),
            scheduled.Count == 0 ? null : scheduled.Max(x => x.EndsAt),
            scheduled.Count);
    }
}

/// <summary>Returned when something is added to an event: the new item's id plus the updated event.</summary>
public sealed record CreatedItemView(Guid Id, EventView Event);
