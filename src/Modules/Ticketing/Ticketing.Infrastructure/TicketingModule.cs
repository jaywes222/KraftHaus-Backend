using Common.Application.Options;
using Common.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Infrastructure.Endpoints;
using Ticketing.Infrastructure.RateLimiting;

namespace Ticketing.Infrastructure;

/// <summary>
///     Ticketing module (Spec 08 §4): waitlist, capacity buckets and holds, orders, tickets; check-in, transfers and
///     Lipa Mdogo Mdogo plans follow. Tenant-scoped: its schema lives in every tenant database and is applied by tenant
///     provisioning (Migrator <c>initialize-all</c> / <c>provision</c>, and the development seeder), not at start-up.
/// </summary>
public sealed class TicketingModule : IModule
{
    public string Name => "Ticketing";

    public int StartupPriority => 40;

    public IEnumerable<Action<RateLimiterOptions, CustomRateLimitingOptions>> RateLimitingPolicies =>
        TicketingRateLimitingPolicies.Policies;

    public IEnumerable<string> ActivitySourceNames => ["Krafthaus.Ticketing"];

    public IEnumerable<string> MeterNames => ["Krafthaus.Ticketing"];

    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddTicketing(configuration);

    public void UseModule(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapClientWaitlistEndpoints();
        endpoints.MapClientOrderEndpoints();
        endpoints.MapAdminTicketingEndpoints();
        endpoints.MapOpsCheckInEndpoints();
    }
}
