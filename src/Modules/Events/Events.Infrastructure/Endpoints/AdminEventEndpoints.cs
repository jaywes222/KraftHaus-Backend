using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Events.Application;
using Events.Application.Events;
using Events.Application.Venues;
using Events.Domain.Common;
using Events.Domain.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Events.Infrastructure.Endpoints;

/// <summary>
///     Admin surface for UF-016 (event setup): the venue library and events with their venue copy, entrances,
///     dates, tiers, sale phases, group offers, policies and lifecycle. All data lives in the tenant database of the
///     host the request came in on; nothing in a request names a tenant.
/// </summary>
internal static class AdminEventEndpoints
{
    public static void MapAdminEventEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin").WithTags("Events (admin)");

        MapVenues(admin.MapGroup("/venues"));
        MapEvents(admin.MapGroup("/events"));
    }

    private static void MapVenues(RouteGroupBuilder venues)
    {
        venues.MapGet("/", (bool? includeArchived, VenueService service, CancellationToken ct) =>
                service.ListAsync(includeArchived ?? false, ct))
            .RequirePermission(EventsPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<VenueView>>>();

        venues.MapGet("/{venueId:guid}", (Guid venueId, VenueService service, CancellationToken ct) =>
                service.GetAsync(new VenueId(venueId), ct))
            .RequirePermission(EventsPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<VenueView>>();

        venues.MapPost("/", (VenueInput input, VenueService service, CancellationToken ct) =>
                service.CreateAsync(input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<VenueView>>();

        venues.MapPut("/{venueId:guid}", (Guid venueId, VenueInput input, VenueService service, CancellationToken ct) =>
                service.UpdateAsync(new VenueId(venueId), input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<VenueView>>();

        venues.MapPost(
                "/{venueId:guid}/entrances",
                (Guid venueId, EntranceInput input, VenueService service, CancellationToken ct) =>
                    service.AddEntranceAsync(new VenueId(venueId), input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<VenueView>>();

        venues.MapPut(
                "/{venueId:guid}/entrances/{entranceId:guid}",
                (Guid venueId, Guid entranceId, EntranceInput input, VenueService service, CancellationToken ct) =>
                    service.UpdateEntranceAsync(new VenueId(venueId), new VenueEntranceId(entranceId), input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<VenueView>>();

        venues.MapDelete(
                "/{venueId:guid}/entrances/{entranceId:guid}",
                (Guid venueId, Guid entranceId, VenueService service, CancellationToken ct) =>
                    service.RemoveEntranceAsync(new VenueId(venueId), new VenueEntranceId(entranceId), ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<VenueView>>();

        venues.MapPost("/{venueId:guid}/archive", (Guid venueId, VenueService service, CancellationToken ct) =>
                service.ArchiveAsync(new VenueId(venueId), ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<VenueView>>();
    }

    private static void MapEvents(RouteGroupBuilder events)
    {
        // Event list and details.
        events.MapGet("/", (EventStatus? status, EventService service, CancellationToken ct) =>
                service.ListAsync(status, ct))
            .RequirePermission(EventsPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<EventSummaryView>>>();

        events.MapGet("/{eventId:guid}", (Guid eventId, EventService service, CancellationToken ct) =>
                service.GetAsync(new EventId(eventId), ct))
            .RequirePermission(EventsPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        events.MapPost("/", (EventDetailsInput input, EventService service, CancellationToken ct) =>
                service.CreateAsync(input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<EventView>>();

        events.MapPut(
                "/{eventId:guid}",
                (Guid eventId, EventDetailsInput input, EventService service, CancellationToken ct) =>
                    service.UpdateDetailsAsync(new EventId(eventId), input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        events.MapPut(
                "/{eventId:guid}/policies",
                (Guid eventId, PoliciesInput input, EventService service, CancellationToken ct) =>
                    service.UpdatePoliciesAsync(new EventId(eventId), input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        // Venue copy.
        events.MapPut(
                "/{eventId:guid}/venue/library",
                (Guid eventId, UseVenueInput input, EventService service, CancellationToken ct) =>
                    service.UseVenueAsync(new EventId(eventId), input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        events.MapPut(
                "/{eventId:guid}/venue/inline",
                (Guid eventId, InlineVenueInput input, EventService service, CancellationToken ct) =>
                    service.UseInlineVenueAsync(new EventId(eventId), input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        events.MapPut(
                "/{eventId:guid}/venue",
                (Guid eventId, InlineVenueInput input, EventService service, CancellationToken ct) =>
                    service.UpdateVenueAsync(new EventId(eventId), input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        events.MapPost(
                "/{eventId:guid}/entrances",
                (Guid eventId, EntranceInput input, EventService service, CancellationToken ct) =>
                    service.AddEntranceAsync(new EventId(eventId), input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<CreatedItemView>>();

        events.MapPut(
                "/{eventId:guid}/entrances/{entranceId:guid}",
                (Guid eventId, Guid entranceId, EntranceInput input, EventService service, CancellationToken ct) =>
                    service.UpdateEntranceAsync(new EventId(eventId), new EventEntranceId(entranceId), input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        events.MapDelete(
                "/{eventId:guid}/entrances/{entranceId:guid}",
                (Guid eventId, Guid entranceId, EventService service, CancellationToken ct) =>
                    service.RemoveEntranceAsync(new EventId(eventId), new EventEntranceId(entranceId), ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        MapDates(events.MapGroup("/{eventId:guid}/dates"));

        // Lifecycle.
        events.MapPost("/{eventId:guid}/publish", (Guid eventId, EventService service, CancellationToken ct) =>
                service.PublishAsync(new EventId(eventId), ct))
            .RequirePermission(EventsPermission.Publish)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        events.MapPost(
                "/{eventId:guid}/cancel",
                (Guid eventId, CancelEventInput input, EventService service, CancellationToken ct) =>
                    service.CancelAsync(new EventId(eventId), input, ct))
            .RequirePermission(EventsPermission.Publish)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        events.MapPost("/{eventId:guid}/complete", (Guid eventId, EventService service, CancellationToken ct) =>
                service.CompleteAsync(new EventId(eventId), ct))
            .RequirePermission(EventsPermission.Publish)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();
    }

    private static void MapDates(RouteGroupBuilder dates)
    {
        dates.MapPost("/", (Guid eventId, DateInput input, EventService service, CancellationToken ct) =>
                service.AddDateAsync(new EventId(eventId), input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<CreatedItemView>>();

        dates.MapPut(
                "/{dateId:guid}",
                (Guid eventId, Guid dateId, DateInput input, EventService service, CancellationToken ct) =>
                    service.UpdateDateAsync(new EventId(eventId), new EventDateId(dateId), input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        dates.MapDelete("/{dateId:guid}", (Guid eventId, Guid dateId, EventService service, CancellationToken ct) =>
                service.RemoveDateAsync(new EventId(eventId), new EventDateId(dateId), ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        dates.MapPost(
                "/{dateId:guid}/cancel",
                (Guid eventId, Guid dateId, EventService service, CancellationToken ct) =>
                    service.CancelDateAsync(new EventId(eventId), new EventDateId(dateId), ct))
            .RequirePermission(EventsPermission.Publish)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        MapTiers(dates.MapGroup("/{dateId:guid}/tiers"));
    }

    private static void MapTiers(RouteGroupBuilder tiers)
    {
        tiers.MapPost(
                "/",
                (Guid eventId, Guid dateId, TierInput input, EventService service, CancellationToken ct) =>
                    service.AddTierAsync(new EventId(eventId), new EventDateId(dateId), input, ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<CreatedItemView>>();

        tiers.MapPut(
                "/{tierId:guid}",
                (Guid eventId, Guid dateId, Guid tierId, TierInput input, EventService service, CancellationToken ct) =>
                    service.UpdateTierAsync(
                        new EventId(eventId),
                        new EventDateId(dateId),
                        new TicketTierId(tierId),
                        input,
                        ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        tiers.MapDelete(
                "/{tierId:guid}",
                (Guid eventId, Guid dateId, Guid tierId, EventService service, CancellationToken ct) =>
                    service.RemoveTierAsync(new EventId(eventId), new EventDateId(dateId), new TicketTierId(tierId), ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        tiers.MapPost(
                "/{tierId:guid}/withdraw",
                (Guid eventId, Guid dateId, Guid tierId, EventService service, CancellationToken ct) =>
                    service.WithdrawTierAsync(
                        new EventId(eventId),
                        new EventDateId(dateId),
                        new TicketTierId(tierId),
                        ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        MapPhases(tiers.MapGroup("/{tierId:guid}/phases"));
        MapOffers(tiers.MapGroup("/{tierId:guid}/offers"));
    }

    private static void MapPhases(RouteGroupBuilder phases)
    {
        phases.MapPost(
                "/",
                (Guid eventId, Guid dateId, Guid tierId, PhaseInput input, EventService service, CancellationToken ct) =>
                    service.AddPhaseAsync(
                        new EventId(eventId),
                        new EventDateId(dateId),
                        new TicketTierId(tierId),
                        input,
                        ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<CreatedItemView>>();

        phases.MapPut(
                "/{phaseId:guid}",
                (Guid eventId,
                    Guid dateId,
                    Guid tierId,
                    Guid phaseId,
                    PhaseInput input,
                    EventService service,
                    CancellationToken ct) =>
                    service.UpdatePhaseAsync(
                        new EventId(eventId),
                        new EventDateId(dateId),
                        new TicketTierId(tierId),
                        new SalePhaseId(phaseId),
                        input,
                        ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        phases.MapDelete(
                "/{phaseId:guid}",
                (Guid eventId, Guid dateId, Guid tierId, Guid phaseId, EventService service, CancellationToken ct) =>
                    service.RemovePhaseAsync(
                        new EventId(eventId),
                        new EventDateId(dateId),
                        new TicketTierId(tierId),
                        new SalePhaseId(phaseId),
                        ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();
    }

    private static void MapOffers(RouteGroupBuilder offers)
    {
        offers.MapPost(
                "/",
                (Guid eventId,
                    Guid dateId,
                    Guid tierId,
                    GroupOfferInput input,
                    EventService service,
                    CancellationToken ct) =>
                    service.AddGroupOfferAsync(
                        new EventId(eventId),
                        new EventDateId(dateId),
                        new TicketTierId(tierId),
                        input,
                        ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<CreatedItemView>>();

        offers.MapPut(
                "/{offerId:guid}",
                (Guid eventId,
                    Guid dateId,
                    Guid tierId,
                    Guid offerId,
                    GroupOfferInput input,
                    EventService service,
                    CancellationToken ct) =>
                    service.UpdateGroupOfferAsync(
                        new EventId(eventId),
                        new EventDateId(dateId),
                        new TicketTierId(tierId),
                        new GroupOfferId(offerId),
                        input,
                        ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        offers.MapDelete(
                "/{offerId:guid}",
                (Guid eventId, Guid dateId, Guid tierId, Guid offerId, EventService service, CancellationToken ct) =>
                    service.RemoveGroupOfferAsync(
                        new EventId(eventId),
                        new EventDateId(dateId),
                        new TicketTierId(tierId),
                        new GroupOfferId(offerId),
                        ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();

        offers.MapPost(
                "/{offerId:guid}/withdraw",
                (Guid eventId, Guid dateId, Guid tierId, Guid offerId, EventService service, CancellationToken ct) =>
                    service.WithdrawGroupOfferAsync(
                        new EventId(eventId),
                        new EventDateId(dateId),
                        new TicketTierId(tierId),
                        new GroupOfferId(offerId),
                        ct))
            .RequirePermission(EventsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<EventView>>();
    }
}
