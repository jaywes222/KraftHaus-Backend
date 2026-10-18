using Common.Application.Analytics;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Music.Application;
using Music.Application.Abstractions;
using Music.Application.Catalog;
using Music.Application.Requests;
using Music.Infrastructure.Catalog;
using Music.Infrastructure.Database;
using Music.Infrastructure.Persistence.Repositories;

namespace Music.Infrastructure;

public static class Setup
{
    public static IServiceCollection AddMusic(this IServiceCollection services)
    {
        services.AddMusicPersistence();

        services.AddScoped<IRequestBoardRepository, RequestBoardRepository>();
        services.AddScoped<ISongRequestRepository, SongRequestRepository>();
        services.AddScoped<IMusicUnitOfWork, MusicUnitOfWork>();

        services.AddOptions<MusicOptions>().BindConfiguration(MusicOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<MusicOptions>, MusicOptionsValidator>();

        services.AddMemoryCache();
        services.AddSingleton<MusicBrainzGate>();
        services
            .AddHttpClient<IMusicCatalog, MusicBrainzCatalog>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<MusicOptions>>().Value;

                client.BaseAddress = options.MusicBrainzBaseUrl;
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // MusicBrainz blocks callers without a meaningful User-Agent.
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    string.IsNullOrWhiteSpace(options.UserAgent) ? "KRAFTHAUS-dev/0.1 ( development )" : options.UserAgent);
            });

        services.AddScoped<SongRequestService>();
        services.AddScoped<Common.Application.Music.IMusicBoard, MusicBoardProvider>();
        services.AddScoped<IMusicInsights, MusicInsightsProvider>();

        return services;
    }
}
