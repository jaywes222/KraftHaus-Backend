using Common.Application.Analytics;
using Common.Application.Options;
using Common.Infrastructure.Modules;
using Feedback.Application;
using Feedback.Infrastructure.Database;
using Feedback.Infrastructure.Endpoints;
using Feedback.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Feedback.Infrastructure;

/// <summary>
///     Feedback module (UF-010): anonymous ratings of each event date by guests who were checked in, de-duplicated by a
///     one-way hash (BR-027). Tenant-scoped.
/// </summary>
public sealed class FeedbackModule : IModule
{
    public string Name => "Feedback";

    public int StartupPriority => 75;

    public IEnumerable<Action<RateLimiterOptions, CustomRateLimitingOptions>> RateLimitingPolicies => [];

    public IEnumerable<string> ActivitySourceNames => ["Krafthaus.Feedback"];

    public IEnumerable<string> MeterNames => ["Krafthaus.Feedback"];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddFeedbackPersistence();

        services.AddOptions<FeedbackOptions>().BindConfiguration(FeedbackOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IPostConfigureOptions<FeedbackOptions>, FeedbackDevelopmentSecret>();
        services.AddSingleton<IValidateOptions<FeedbackOptions>, FeedbackOptionsValidator>();

        services.AddScoped<IRatingRepository, RatingRepository>();
        services.AddScoped<IFeedbackUnitOfWork, FeedbackUnitOfWork>();
        services.AddSingleton<RatingHasher>();
        services.AddScoped<FeedbackService>();
        services.AddScoped<IFeedbackInsights, FeedbackInsightsProvider>();
    }

    public void UseModule(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapFeedbackEndpoints();
}
