using Commerce.Infrastructure.Endpoints;
using Commerce.Infrastructure.RateLimiting;
using Common.Application.Options;
using Common.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Commerce.Infrastructure;

/// <summary>
///     Commerce module (Spec 08 §4): Krafts, brands, Krafters, menus, stock, live orders and reservations, the kitchen
///     board and pickup. Tenant-scoped: its schema lives in every tenant database and is applied by tenant provisioning
///     (Migrator <c>initialize-all</c> / <c>provision</c>, and the development seeder), not at start-up.
/// </summary>
public sealed class CommerceModule : IModule
{
    public string Name => "Commerce";

    public int StartupPriority => 45;

    public IEnumerable<Action<RateLimiterOptions, CustomRateLimitingOptions>> RateLimitingPolicies =>
        CommerceRateLimitingPolicies.Policies;

    public IEnumerable<string> ActivitySourceNames => ["Krafthaus.Commerce"];

    public IEnumerable<string> MeterNames => ["Krafthaus.Commerce"];

    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddCommerce(configuration);

    public void UseModule(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapAdminKraftEndpoints();
        endpoints.MapClientKraftEndpoints();
        endpoints.MapMenuEndpoints();
        endpoints.MapOrderEndpoints();
    }
}
