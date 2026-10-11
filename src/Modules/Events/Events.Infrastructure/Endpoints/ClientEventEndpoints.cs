using Common.Application.EndpointFilters;
using Events.Application.Public;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Events.Infrastructure.Endpoints;

/// <summary>Public client surface: anyone can browse the host tenant's published events (no sign-in).</summary>
internal static class ClientEventEndpoints
{
    public static void MapClientEventEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var events = endpoints.MapGroup("/api/client/events").WithTags("Events (client)").AllowAnonymous();

        events.MapGet("/", (PublicEventService service, CancellationToken ct) => service.ListUpcomingAsync(ct))
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<PublicEventCard>>>();

        events.MapGet("/{slug}", (string slug, PublicEventService service, CancellationToken ct) =>
                service.GetBySlugAsync(slug, ct))
            .AddEndpointFilter<ResultToResponseTransformer<PublicEvent>>();
    }
}
