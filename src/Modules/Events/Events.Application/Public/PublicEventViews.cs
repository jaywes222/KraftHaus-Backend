using Common.Domain.Currency;
using Events.Domain.Model;

namespace Events.Application.Public;

// -------------------------------------------------------------------------------------------------------------
// Customer-facing views. Deliberately leave out capacities, allocations, draft-only data and anything that is not
// on sale; field-level restrictions are separate DTOs, not conditional hiding (Spec 08 §6.2).
// -------------------------------------------------------------------------------------------------------------

public sealed record PublicEventCard(
    string Slug,
    string Title,
    string? Summary,
    Uri? PosterUrl,
    string? VenueName,
    string? City,
    DateTimeOffset? NextStartsAt,
    Money? FromPrice);

public sealed record PublicVenue(
    string Name,
    string? AddressLine,
    string? City,
    Uri? MapUrl,
    string? Directions,
    IReadOnlyList<string> Entrances);

/// <param name="EarlyAccess">Only phone numbers on the event's waitlist can buy at this price.</param>
public sealed record PublicPrice(string PhaseName, Money Price, DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool EarlyAccess);

public sealed record PublicGroupOffer(
    Guid Id,
    GroupOfferKind Kind,
    string Name,
    int Admits,
    Money Price,
    DateTimeOffset EndsAt);

public sealed record PublicTier(
    Guid Id,
    string Name,
    string? Description,
    bool OnSale,
    PublicPrice? CurrentPrice,
    PublicPrice? NextPrice,
    IReadOnlyList<PublicGroupOffer> GroupOffers);

public sealed record PublicDate(
    Guid Id,
    string? Label,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset? DoorsOpenAt,
    bool Cancelled,
    IReadOnlyList<PublicTier> Tiers);

/// <param name="MaxTicketsPerOrder">Admissions one order may hold (a Couple bundle counts 2), so the picker can cap quantities.</param>
/// <param name="HoldMinutes">How long seats are held while the M-Pesa prompt is open.</param>
public sealed record PublicPolicies(
    bool RefundsEnabled,
    bool TransfersEnabled,
    bool LipaMdogoMdogoEnabled,
    int MaxTicketsPerOrder,
    int HoldMinutes);

public sealed record PublicEvent(
    string Slug,
    string Title,
    string? Summary,
    string? Description,
    Uri? PosterUrl,
    EventStatus Status,
    string? CancellationReason,
    PublicVenue? Venue,
    PublicPolicies Policies,
    IReadOnlyList<PublicDate> Dates);

internal static class PublicEventMapping
{
    public static PublicEventCard ToCard(this Event @event, DateTimeOffset now)
    {
        var upcoming = @event.Dates
            .Where(x => x.Status == EventDateStatus.Scheduled && x.EndsAt > now)
            .ToList();

        var prices = upcoming
            .SelectMany(date => date.Tiers.Where(tier => tier.Status == SaleItemStatus.Active))
            .Select(tier => tier.CurrentPhase(now) ?? tier.NextPhase(now))
            .Where(phase => phase is not null)
            .Select(phase => phase!.Price)
            .ToList();

        return new PublicEventCard(
            @event.Slug,
            @event.Title,
            @event.Summary,
            @event.PosterUrl,
            @event.Venue?.Details.Name,
            @event.Venue?.Details.City,
            upcoming.Count == 0 ? null : upcoming.Min(x => x.StartsAt),
            prices.Count == 0 ? null : prices.Min());
    }

    public static PublicEvent ToPublic(this Event @event, DateTimeOffset now) =>
        new(
            @event.Slug,
            @event.Title,
            @event.Summary,
            @event.Description,
            @event.PosterUrl,
            @event.Status,
            @event.CancellationReason,
            @event.Venue is { } venue
                ? new PublicVenue(
                    venue.Details.Name,
                    venue.Details.AddressLine,
                    venue.Details.City,
                    venue.Details.MapUrl,
                    venue.Details.Directions,
                    [.. @event.Entrances.Select(x => x.Name)])
                : null,
            new PublicPolicies(
                @event.Policies.RefundsEnabled,
                @event.Policies.TransfersEnabled,
                @event.Policies.LipaMdogoMdogoEnabled,
                @event.Policies.MaxTicketsPerOrder,
                @event.Policies.HoldMinutes),
            [
                .. @event.Dates
                    .OrderBy(x => x.StartsAt)
                    .Select(date => new PublicDate(
                        date.Id.Value,
                        date.Label,
                        date.StartsAt,
                        date.EndsAt,
                        date.DoorsOpenAt,
                        date.Status == EventDateStatus.Cancelled,
                        date.Status == EventDateStatus.Scheduled
                            ? [.. date.Tiers
                                .Where(x => x.Status == SaleItemStatus.Active)
                                .OrderBy(x => x.Position)
                                .Select(tier => tier.ToPublic(@event.Status, now))]
                            : []))
            ]);

    private static PublicTier ToPublic(this TicketTier tier, EventStatus status, DateTimeOffset now)
    {
        var selling = status == EventStatus.Published;
        var current = tier.CurrentPhase(now);
        var next = tier.NextPhase(now);

        var offers = selling
            ? tier.Offers
                .Where(x => x.Status == SaleItemStatus.Active && x.Window.Contains(now))
                .OrderBy(x => x.Admits)
                .Select(x => new PublicGroupOffer(x.Id.Value, x.Kind, x.Name, x.Admits, x.Price, x.EndsAt))
                .ToList()
            : [];

        return new PublicTier(
            tier.Id.Value,
            tier.Name,
            tier.Description,
            selling && (current is not null || offers.Count > 0),
            selling && current is not null ? ToPrice(current) : null,
            selling && next is not null ? ToPrice(next) : null,
            offers);
    }

    private static PublicPrice ToPrice(SalePhase phase) =>
        new(phase.Name, phase.Price, phase.StartsAt, phase.EndsAt, phase.EarlyAccess);
}
