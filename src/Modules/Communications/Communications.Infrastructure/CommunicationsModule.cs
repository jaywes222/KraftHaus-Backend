using Common.Application.Options;
using Common.Infrastructure.Modules;
using Communications.Infrastructure.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Communications.Infrastructure;

/// <summary>
///     Communications module (UF-020): a row per SMS / e-mail with delivery status, broadcasts to event audiences sent
///     by a throttled dispatcher, saved templates and marketing opt-outs. Tenant-scoped: its schema lives in every
///     tenant database and is applied by tenant provisioning.
/// </summary>
public sealed class CommunicationsModule : IModule
{
    public string Name => "Communications";

    public int StartupPriority => 70;

    public IEnumerable<Action<RateLimiterOptions, CustomRateLimitingOptions>> RateLimitingPolicies => [];

    public IEnumerable<string> ActivitySourceNames => ["Krafthaus.Communications"];

    public IEnumerable<string> MeterNames => ["Krafthaus.Communications"];

    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddCommunications(configuration);

    public void UseModule(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCommunicationsAdminEndpoints();
        endpoints.MapCommunicationsClientEndpoints();
        endpoints.MapCommunicationsWebhooks();
    }
}
