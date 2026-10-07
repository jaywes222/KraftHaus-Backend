using Common.Infrastructure.Extensions;
using Common.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tenancy.Infrastructure.Persistence;
using Tenancy.Infrastructure.Resolution;

namespace Tenancy.Infrastructure;

public sealed class TenancyModule : ICoreModule
{
    public string Name => "Tenancy";
    public int StartupPriority => 10;

    public IEnumerable<string> ActivitySourceNames =>
        ["Krafthaus.Tenancy"];

    public IEnumerable<string> MeterNames =>
        ["Krafthaus.Tenancy"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
        => services.AddTenancy();

    public void UseModule(IApplicationBuilder app)
    {
        app.UseMigration<TenancyDbContext>();
        app.UseTenantResolution();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
