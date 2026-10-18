using Common.Application.Options;
using Common.Infrastructure.Modules;
using Displays.Infrastructure.Endpoints;
using Displays.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Displays.Infrastructure;

/// <summary>
///     Displays module (Spec 08 §4, UF-019): pairing, device tokens, configurations, time schedules, the programme and
///     images for screens at the venue. Tenant-scoped: its schema lives in every tenant database and is applied by
///     tenant provisioning (Migrator <c>initialize-all</c> / <c>provision</c>, and the development seeder).
/// </summary>
public sealed class DisplaysModule : IModule
{
    public string Name => "Displays";

    public int StartupPriority => 50;

    public IEnumerable<Action<RateLimiterOptions, CustomRateLimitingOptions>> RateLimitingPolicies =>
        DisplaysRateLimitingPolicies.Policies;

    public IEnumerable<string> ActivitySourceNames => ["Krafthaus.Displays"];

    public IEnumerable<string> MeterNames => ["Krafthaus.Displays"];

    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddDisplays();

    public void UseModule(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDisplayDeviceEndpoints();
        endpoints.MapDisplayAdminEndpoints();
    }
}
