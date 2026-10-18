using Common.Application.Options;
using Common.Infrastructure.Modules;
using Content.Application;
using Content.Infrastructure.Database;
using Content.Infrastructure.Endpoints;
using Content.Infrastructure.Persistence;
using Content.Infrastructure.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Content.Infrastructure;

/// <summary>Content module (UF-024): articles with covers, and the Web Studio's site blocks. Tenant-scoped.</summary>
public sealed class ContentModule : IModule
{
    public string Name => "Content";

    public int StartupPriority => 80;

    public IEnumerable<Action<RateLimiterOptions, CustomRateLimitingOptions>> RateLimitingPolicies => [];

    public IEnumerable<string> ActivitySourceNames => ["Krafthaus.Content"];

    public IEnumerable<string> MeterNames => ["Krafthaus.Content"];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddContentPersistence();

        services.AddOptions<ContentOptions>().BindConfiguration(ContentOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ContentOptions>, ContentOptionsValidator>();

        services.AddScoped<IArticleRepository, ArticleRepository>();
        services.AddScoped<ISiteBlockRepository, SiteBlockRepository>();
        services.AddScoped<IContentUnitOfWork, ContentUnitOfWork>();
        services.AddScoped<IContentImageStorage, DiskContentImageStorage>();
        services.AddScoped<ArticleService>();
        services.AddScoped<SiteStudioService>();
    }

    public void UseModule(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapContentEndpoints();
}
