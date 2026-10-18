using Analytics.Application;
using Analytics.Domain;
using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Common.Application.Options;
using Common.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Analytics.Infrastructure;

/// <summary>
///     Analytics module (UF-023): the event dashboard and the public leaderboard, composed from the other modules'
///     insights interfaces. It owns no tables: the "read models" are short-lived cached projections.
/// </summary>
public sealed class AnalyticsModule : IModule
{
    public string Name => "Analytics";

    public int StartupPriority => 90;

    public IEnumerable<Action<RateLimiterOptions, CustomRateLimitingOptions>> RateLimitingPolicies => [];

    public IEnumerable<string> ActivitySourceNames => ["Krafthaus.Analytics"];

    public IEnumerable<string> MeterNames => ["Krafthaus.Analytics"];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AnalyticsOptions>().BindConfiguration(AnalyticsOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AnalyticsOptions>, AnalyticsOptionsValidator>();
        services.AddMemoryCache();
        services.AddScoped<EventDashboardService>();
        services.AddScoped<LeaderboardService>();
    }

    public void UseModule(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/events/{eventId:guid}/dashboard", (Guid eventId, EventDashboardService service, CancellationToken ct) =>
                service.GetAsync(eventId, ct))
            .WithTags("Analytics (admin)")
            .RequirePermission(AnalyticsPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<EventDashboardView>>();

        endpoints.MapGet("/api/client/events/{slug}/leaderboard", (string slug, LeaderboardService service, CancellationToken ct) =>
                service.GetAsync(slug, ct))
            .WithTags("Analytics (client)")
            .AllowAnonymous()
            .AddEndpointFilter<ResultToResponseTransformer<LeaderboardView>>();
    }
}
