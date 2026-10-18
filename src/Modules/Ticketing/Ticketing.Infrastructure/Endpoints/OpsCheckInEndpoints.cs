using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ticketing.Application.CheckIn;
using Ticketing.Domain.Common;
using Ticketing.Infrastructure.RateLimiting;

namespace Ticketing.Infrastructure.Endpoints;

/// <summary>Event Ops surface for door staff (UF-012 QR, UF-013 manual), <c>tickets:checkin</c>.</summary>
internal static class OpsCheckInEndpoints
{
    public static void MapOpsCheckInEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var ops = endpoints.MapGroup("/api/ops/check-in")
            .WithTags("Check-in (ops)")
            .RequirePermission(TicketingPermission.CheckIn);

        ops.MapPost("/scan", (CheckInScanRequest request, ICurrentUser user, CheckInService service, CancellationToken ct) =>
                service.ScanAsync(user.Id, request, ct))
            .RequireRateLimiting(TicketingRateLimitingPolicies.Scan)
            .AddEndpointFilter<ResultToResponseTransformer<CheckInResultView>>();

        ops.MapPost("/manual", (CheckInManualRequest request, ICurrentUser user, CheckInService service, CancellationToken ct) =>
                service.ManualAsync(user.Id, request, ct))
            .RequireRateLimiting(TicketingRateLimitingPolicies.Scan)
            .AddEndpointFilter<ResultToResponseTransformer<CheckInResultView>>();

        ops.MapGet("/dates/{eventDateId:guid}/search", (Guid eventDateId, string? q, CheckInService service, CancellationToken ct) =>
                service.SearchAsync(eventDateId, q, ct))
            .RequireRateLimiting(TicketingRateLimitingPolicies.Scan)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<CheckInSearchHit>>>();

        ops.MapGet("/dates/{eventDateId:guid}/stats", (Guid eventDateId, CheckInService service, CancellationToken ct) =>
                service.StatsAsync(eventDateId, ct))
            .AddEndpointFilter<ResultToResponseTransformer<CheckInStatsView>>();
    }
}
