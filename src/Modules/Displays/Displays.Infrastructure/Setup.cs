using Common.Application.Displays;
using Displays.Application;
using Displays.Application.Abstractions;
using Displays.Application.Assets;
using Displays.Application.Configurations;
using Displays.Application.Devices;
using Displays.Application.Feed;
using Displays.Application.Programme;
using Displays.Infrastructure.Authentication;
using Displays.Infrastructure.Database;
using Displays.Infrastructure.Persistence.Repositories;
using Displays.Infrastructure.Realtime;
using Displays.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Displays.Infrastructure;

public static class Setup
{
    public static IServiceCollection AddDisplays(this IServiceCollection services)
    {
        services.AddDisplaysPersistence();

        services.AddScoped<IDisplayDeviceRepository, DisplayDeviceRepository>();
        services.AddScoped<IDisplayPairingRepository, DisplayPairingRepository>();
        services.AddScoped<IDisplayConfigurationRepository, DisplayConfigurationRepository>();
        services.AddScoped<IProgrammeRepository, ProgrammeRepository>();
        services.AddScoped<IDisplayAssetRepository, DisplayAssetRepository>();
        services.AddScoped<IDisplaysUnitOfWork, DisplaysUnitOfWork>();
        services.AddScoped<IAssetStorage, DiskAssetStorage>();

        services.AddOptions<DisplaysOptions>().BindConfiguration(DisplaysOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<DisplaysOptions>, DisplaysOptionsValidator>();

        services.AddDataProtection();
        services.AddMemoryCache();
        services.AddSingleton<IPairingTokenProtector, PairingTokenProtector>();
        services.AddScoped<DeviceTokenCache>();
        services.AddScoped<IDeviceTokenCache>(sp => sp.GetRequiredService<DeviceTokenCache>());
        services.AddScoped<DeviceAuthenticator>();
        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, DisplayDeviceAuthenticationHandler>(DisplayDeviceAuthenticationHandler.SchemeName, null);

        services.AddSignalR();
        services.AddScoped<IDisplayRealtime, SignalRDisplayRealtime>();
        services.AddScoped<IDisplayBoardSignal, DisplayBoardSignal>();

        services.AddScoped<PairingService>();
        services.AddScoped<DeviceAdminService>();
        services.AddScoped<ConfigurationService>();
        services.AddScoped<ProgrammeService>();
        services.AddScoped<AssetService>();
        services.AddScoped<Displays.Application.Gallery.IGalleryRepository, Persistence.Repositories.GalleryRepository>();
        services.AddScoped<Displays.Application.Gallery.GalleryService>();
        services.AddScoped<FeedService>();

        return services;
    }
}
