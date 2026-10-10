using Common.Application.Options;
using Common.Infrastructure.Extensions;
using Common.Infrastructure.Modules;
using Identity.Infrastructure.Endpoints;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure;

public sealed class IdentityModule : ICoreModule
{
    public string Name => "Identity";

    public int StartupPriority => 20;

    public IEnumerable<string> ActivitySourceNames =>
        ["Krafthaus.Identity"];

    public IEnumerable<string> MeterNames =>
        ["Krafthaus.Identity"];

    public IEnumerable<
    Action<RateLimiterOptions, CustomRateLimitingOptions>>?
    RateLimitingPolicies =>
    IdentityRateLimitingPolicies.Policies;

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
        => services.AddIdentity(configuration);

    public void UseModule(IApplicationBuilder app)
    {
        app.UseMigration<IdentityDbContext>();

        app.UseIdentityServer();
    }

    public void MapEndpoints(
        IEndpointRouteBuilder endpoints)
    {
        endpoints.MapIdentityEndpoints();
    }
}
