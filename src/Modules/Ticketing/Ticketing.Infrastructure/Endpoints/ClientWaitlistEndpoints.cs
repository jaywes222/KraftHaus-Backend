using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Ticketing.Application.Orders;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ticketing.Application.Waitlist;
using Ticketing.Infrastructure.RateLimiting;

namespace Ticketing.Infrastructure.Endpoints;

/// <summary>Public client surface: anyone can join the host tenant's event waitlist (UF-001, no sign-in).</summary>
internal static class ClientWaitlistEndpoints
{
    public static void MapClientWaitlistEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var waitlist = endpoints.MapGroup("/api/client/waitlist").WithTags("Waitlist (client)").AllowAnonymous();

        waitlist.MapPost("/", (JoinWaitlistRequest request, WaitlistService service, CancellationToken ct) =>
                service.JoinAsync(request, ct))
            .RequireRateLimiting(TicketingRateLimitingPolicies.Waitlist)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<WaitlistJoinedView>>();

        // Public: the standard fee for anyone, the waiver when the signed-in caller's phone is on the waitlist.
        endpoints.MapGet("/api/client/events/{slug}/fees", (string slug, ICurrentUser user, FeeQuoteService service, CancellationToken ct) =>
                service.QuoteAsync(slug, user.Id.Value == Guid.Empty ? null : user.Id, ct))
            .WithTags("Waitlist (client)")
            .AllowAnonymous()
            .AddEndpointFilter<ResultToResponseTransformer<FeeQuoteView>>();
    }
}
