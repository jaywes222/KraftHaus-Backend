using Common.Infrastructure.Modules;
using Events.Infrastructure.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Events.Infrastructure;

/// <summary>
///     Events module (Spec 08 §4): venues, events, dates, tiers, sale phases, group offers, policies, lifecycle.
///     Tenant-scoped: its schema lives in every tenant database and is applied by tenant provisioning (Migrator
///     <c>initialize-all</c> / <c>provision</c>, and the development seeder), not at API start-up.
/// </summary>
public sealed class EventsModule : IModule
{
    public string Name => "Events";

    public int StartupPriority => 30;

    public IEnumerable<string> ActivitySourceNames => ["Krafthaus.Events"];

    public IEnumerable<string> MeterNames => ["Krafthaus.Events"];

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddEvents();

    public void UseModule(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapAdminEventEndpoints();
        endpoints.MapClientEventEndpoints();
    }
}
