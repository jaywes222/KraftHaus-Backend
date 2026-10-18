using Common.Application.EventCatalog;
using Events.Application.Abstractions;
using Events.Domain.Common;
using Events.Domain.Model;
using Events.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Events.Infrastructure.Catalog;

/// <summary>
///     Events' side of <see cref="IEventCatalog" />: a small projection (no aggregate graph), because the waitlist
///     can take a burst of traffic when the link goes out.
/// </summary>
internal sealed class EventCatalog(EventsDbContext db, IEventRepository events) : IEventCatalog
{
    public async Task<EventSnapshot?> FindBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var valid = Event.ValidateSlug(slug);
        if (valid.IsFailure)
        {
            return null;
        }

        var normalized = valid.Value!;

        var row = await db.Events
            .AsNoTracking()
            .TagWith(nameof(EventCatalog) + "." + nameof(FindBySlugAsync))
            .Where(x => x.Slug == normalized)
            .Select(x => new
            {
                x.Id,
                x.Slug,
                x.Title,
                x.Status,
                FirstStartsAt = x.Dates
                    .Where(d => d.Status == EventDateStatus.Scheduled)
                    .Min(d => (DateTimeOffset?)d.StartsAt),
                LastEndsAt = x.Dates
                    .Where(d => d.Status == EventDateStatus.Scheduled)
                    .Max(d => (DateTimeOffset?)d.EndsAt)
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new EventSnapshot(row.Id.Value, row.Slug, row.Title, ToLifecycle(row.Status), row.FirstStartsAt, row.LastEndsAt);
    }

    public async Task<EventSaleSnapshot?> GetSaleSnapshotAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var @event = await events.FindForReadAsync(new EventId(eventId), cancellationToken);
        if (@event is null)
        {
            return null;
        }

        var policies = @event.Policies;

        return new EventSaleSnapshot(
            @event.Id.Value,
            @event.Slug,
            @event.Title,
            @event.Venue?.Details.Name,
            ToLifecycle(@event.Status),
            new EventSalePolicies(
                policies.RefundsEnabled,
                policies.TransfersEnabled,
                policies.MaxTransfersPerTicket,
                policies.LipaMdogoMdogoEnabled,
                policies.HoldMinutes,
                policies.MaxTicketsPerOrder),
            [
                .. @event.Dates.OrderBy(x => x.StartsAt).Select(date => new SaleDate(
                    date.Id.Value,
                    date.Label,
                    date.StartsAt,
                    date.EndsAt,
                    date.DoorsOpenAt,
                    date.Status == EventDateStatus.Cancelled,
                    [
                        .. date.Tiers.OrderBy(x => x.Position).Select(tier => new SaleTier(
                            tier.Id.Value,
                            tier.Name,
                            tier.Capacity,
                            tier.Status == SaleItemStatus.Withdrawn,
                            [
                                .. tier.Phases.OrderBy(x => x.StartsAt).Select(phase => new SalePhaseInfo(
                                    phase.Id.Value,
                                    phase.Name,
                                    phase.Price.Amount,
                                    phase.StartsAt,
                                    phase.EndsAt,
                                    phase.Allocation,
                                    phase.EarlyAccess))
                            ],
                            [
                                .. tier.Offers.OrderBy(x => x.Admits).Select(offer => new SaleOffer(
                                    offer.Id.Value,
                                    offer.Name,
                                    offer.Admits,
                                    offer.Price.Amount,
                                    offer.StartsAt,
                                    offer.EndsAt,
                                    offer.Allocation,
                                    offer.Status == SaleItemStatus.Withdrawn))
                            ]))
                    ]))
            ]);
    }

    private static EventLifecycle ToLifecycle(EventStatus status) =>
        status switch
        {
            EventStatus.Draft => EventLifecycle.Draft,
            EventStatus.Published => EventLifecycle.Published,
            EventStatus.Cancelled => EventLifecycle.Cancelled,
            EventStatus.Completed => EventLifecycle.Completed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown event status.")
        };
}
