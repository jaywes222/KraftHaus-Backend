using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Music.Application.Requests;
using Music.Domain.Common;
using Music.Infrastructure.RateLimiting;

namespace Music.Infrastructure.Endpoints;

/// <summary>Song requests: the public list, search and requests for ticket holders, and the DJ console.</summary>
internal static class MusicEndpoints
{
    public static void MapMusicClientEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var events = endpoints.MapGroup("/api/client/events/{slug}/song-requests").WithTags("Song requests (client)");

        // The list (songs and vote counts, never names) is public, like the event page.
        events.MapGet("/", (string slug, SongRequestService service, CancellationToken ct) =>
                service.GetPublicAsync(slug, ct))
            .AllowAnonymous()
            .AddEndpointFilter<ResultToResponseTransformer<RequestBoardPublicView>>();

        events.MapGet("/mine", (string slug, ICurrentUser user, SongRequestService service, CancellationToken ct) =>
                service.GetMineAsync(slug, user.Id, ct))
            .RequireAuthorization()
            .AddEndpointFilter<ResultToResponseTransformer<MySongRequestsView>>();

        events.MapPost("/", (string slug, CreateSongRequestRequest request, ICurrentUser user, SongRequestService service, CancellationToken ct) =>
                service.RequestAsync(slug, user.Id, request, ct))
            .RequireAuthorization()
            .RequireRateLimiting(MusicRateLimitingPolicies.Request)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<SongRequestResultView>>();

        events.MapPost("/{requestId:guid}/votes", (string slug, Guid requestId, ICurrentUser user, SongRequestService service, CancellationToken ct) =>
                service.VoteAsync(slug, user.Id, requestId, ct))
            .RequireAuthorization()
            .RequireRateLimiting(MusicRateLimitingPolicies.Request)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<SongRequestResultView>>();

        endpoints.MapGet("/api/client/music/search", (string? q, SongRequestService service, CancellationToken ct) =>
                service.SearchAsync(q, ct))
            .WithTags("Song requests (client)")
            .RequireAuthorization()
            .RequireRateLimiting(MusicRateLimitingPolicies.Search)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<CatalogTrackView>>>();
    }

    public static void MapMusicAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin").WithTags("DJ console (admin)");

        admin.MapGet("/events/{eventId:guid}/song-requests", (Guid eventId, SongRequestService service, CancellationToken ct) =>
                service.GetConsoleAsync(eventId, ct))
            .RequirePermission(MusicPermission.ManageRequests)
            .AddEndpointFilter<ResultToResponseTransformer<DjConsoleView>>();

        admin.MapPut("/events/{eventId:guid}/song-requests/board", (Guid eventId, ConfigureBoardRequest request, SongRequestService service, CancellationToken ct) =>
                service.ConfigureBoardAsync(eventId, request, ct))
            .RequirePermission(MusicPermission.ManageRequests)
            .AddEndpointFilter<ResultToResponseTransformer<RequestBoardView>>();

        admin.MapPost("/song-requests/{requestId:guid}/status", (Guid requestId, MoveSongRequestRequest request, ICurrentUser user, SongRequestService service, CancellationToken ct) =>
                service.MoveAsync(requestId, request, user.Id, ct))
            .RequirePermission(MusicPermission.ManageRequests)
            .AddEndpointFilter<ResultToResponseTransformer<DjSongRequestView>>();
    }
}
