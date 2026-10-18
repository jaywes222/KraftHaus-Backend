using Common.Application.Options;
using Common.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Music.Infrastructure.Endpoints;
using Music.Infrastructure.RateLimiting;

namespace Music.Infrastructure;

/// <summary>
///     Music module: song requests. Ticket holders ask the DJs for songs (MusicBrainz catalogue, or typed); the DJ
///     console opens requests and works the queue. Tenant-scoped: its schema lives in every tenant database and is
///     applied by tenant provisioning (Migrator <c>initialize-all</c> / <c>provision</c>, and the development seeder).
/// </summary>
public sealed class MusicModule : IModule
{
    public string Name => "Music";

    public int StartupPriority => 60;

    public IEnumerable<Action<RateLimiterOptions, CustomRateLimitingOptions>> RateLimitingPolicies =>
        MusicRateLimitingPolicies.Policies;

    public IEnumerable<string> ActivitySourceNames => ["Krafthaus.Music"];

    public IEnumerable<string> MeterNames => ["Krafthaus.Music"];

    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddMusic();

    public void UseModule(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMusicClientEndpoints();
        endpoints.MapMusicAdminEndpoints();
    }
}
