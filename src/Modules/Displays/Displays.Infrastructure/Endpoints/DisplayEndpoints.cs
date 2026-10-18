using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Displays.Application.Assets;
using Displays.Application.Configurations;
using Displays.Application.Devices;
using Displays.Application.Feed;
using Displays.Application.Gallery;
using Displays.Application.Programme;
using Displays.Domain.Common;
using Displays.Domain.Devices;
using Displays.Infrastructure.Authentication;
using Displays.Infrastructure.RateLimiting;
using Displays.Infrastructure.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Displays.Infrastructure.Endpoints;

/// <summary>Screens (UF-019): pairing, the device feed and hub, and the admin for screens, content, programme and images.</summary>
internal static class DisplayEndpoints
{
    public static void MapDisplayDeviceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var device = endpoints.MapGroup("/api/display").WithTags("Display screens");

        device.MapPost("/pairings", (PairingService service, CancellationToken ct) => service.StartAsync(ct))
            .AllowAnonymous()
            .RequireRateLimiting(DisplaysRateLimitingPolicies.PairingStart)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<PairingStartView>>();

        device.MapPost("/pairings/{pairingId:guid}/poll", (Guid pairingId, PollPairingRequest request, PairingService service, CancellationToken ct) =>
                service.PollAsync(pairingId, request, ct))
            .AllowAnonymous()
            .RequireRateLimiting(DisplaysRateLimitingPolicies.PairingPoll)
            .AddEndpointFilter<ResultToResponseTransformer<PairingPollView>>();

        device.MapGet("/feed", (HttpContext http, FeedService service, CancellationToken ct) =>
                service.GetAsync(DeviceId(http), ct))
            .RequireAuthorization(DisplayDeviceAuthenticationHandler.Policy)
            .AddEndpointFilter<ResultToResponseTransformer<DisplayFeedView>>();

        endpoints.MapHub<DisplayHub>(DisplayHub.Pattern).RequireAuthorization(DisplayDeviceAuthenticationHandler.Policy);

        endpoints.MapGet(AssetUrls.PathPrefix + "{assetId:guid}", async (Guid assetId, HttpContext http, AssetService service, CancellationToken ct) =>
            {
                var opened = await service.OpenAsync(assetId, ct);
                if (opened.IsFailure || opened.Value is not { } content)
                {
                    return Results.NotFound();
                }

                http.Response.Headers.CacheControl = "public, max-age=86400";
                http.Response.Headers.XContentTypeOptions = "nosniff";

                return Results.Stream(content.Content, content.ContentType, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue(content.ETag));
            })
            .WithTags("Display screens")
            .AllowAnonymous();
    }

    public static void MapDisplayAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var screens = endpoints.MapGroup("/api/admin/displays").WithTags("Displays (admin)");

        screens.MapPost("/pair", (PairDisplayRequest request, ICurrentUser user, PairingService service, CancellationToken ct) =>
                service.ClaimAsync(request, user.Id, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<DisplayDeviceView>>();

        screens.MapGet("/", (Guid? eventId, DisplayDeviceStatus? status, DeviceAdminService service, CancellationToken ct) =>
                service.ListAsync(eventId, status, ct))
            .RequirePermission(DisplaysPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<DisplayDeviceView>>>();

        screens.MapGet("/{deviceId:guid}", (Guid deviceId, DeviceAdminService service, CancellationToken ct) =>
                service.GetAsync(deviceId, ct))
            .RequirePermission(DisplaysPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<DisplayDeviceView>>();

        screens.MapPut("/{deviceId:guid}", (Guid deviceId, UpdateDisplayRequest request, DeviceAdminService service, CancellationToken ct) =>
                service.UpdateAsync(deviceId, request, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<DisplayDeviceView>>();

        screens.MapPut("/{deviceId:guid}/schedule", (Guid deviceId, SetScheduleRequest request, DeviceAdminService service, CancellationToken ct) =>
                service.SetScheduleAsync(deviceId, request, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<DisplayDeviceView>>();

        screens.MapPost("/{deviceId:guid}/revoke", (Guid deviceId, RevokeDisplayRequest request, ICurrentUser user, DeviceAdminService service, CancellationToken ct) =>
                service.RevokeAsync(deviceId, request, user.Id, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<DisplayDeviceView>>();

        var configurations = endpoints.MapGroup("/api/admin/display-configurations").WithTags("Displays (admin)");

        configurations.MapPost("/", (CreateConfigurationRequest request, ConfigurationService service, CancellationToken ct) =>
                service.CreateAsync(request, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<DisplayConfigurationView>>();

        configurations.MapGet("/", (Guid? eventId, bool? includeArchived, ConfigurationService service, CancellationToken ct) =>
                service.ListAsync(eventId, includeArchived ?? false, ct))
            .RequirePermission(DisplaysPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<DisplayConfigurationView>>>();

        configurations.MapGet("/{configurationId:guid}", (Guid configurationId, ConfigurationService service, CancellationToken ct) =>
                service.GetAsync(configurationId, ct))
            .RequirePermission(DisplaysPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<DisplayConfigurationView>>();

        configurations.MapPut("/{configurationId:guid}", (Guid configurationId, UpdateConfigurationRequest request, ConfigurationService service, CancellationToken ct) =>
                service.UpdateAsync(configurationId, request, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<DisplayConfigurationView>>();

        configurations.MapDelete("/{configurationId:guid}", (Guid configurationId, ConfigurationService service, CancellationToken ct) =>
                service.ArchiveAsync(configurationId, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<DisplayConfigurationView>>();

        var programme = endpoints.MapGroup("/api/admin").WithTags("Programme (admin)");

        programme.MapPost("/events/{eventId:guid}/programme", (Guid eventId, ProgrammeSlotRequest request, ProgrammeService service, CancellationToken ct) =>
                service.AddAsync(eventId, request, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<ProgrammeSlotView>>();

        programme.MapGet("/events/{eventId:guid}/programme", (Guid eventId, ProgrammeService service, CancellationToken ct) =>
                service.ListAsync(eventId, ct))
            .RequirePermission(DisplaysPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<ProgrammeSlotView>>>();

        programme.MapPut("/programme/{slotId:guid}", (Guid slotId, ProgrammeSlotRequest request, ProgrammeService service, CancellationToken ct) =>
                service.UpdateAsync(slotId, request, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<ProgrammeSlotView>>();

        programme.MapDelete("/programme/{slotId:guid}", (Guid slotId, ProgrammeService service, CancellationToken ct) =>
                service.RemoveAsync(slotId, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<ProgrammeSlotView>>();

        endpoints.MapGet("/api/client/events/{slug}/programme", (string slug, ProgrammeService service, CancellationToken ct) =>
                service.PublicListAsync(slug, ct))
            .WithTags("Programme (client)")
            .AllowAnonymous()
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<ProgrammeSlotView>>>();

        var assets = endpoints.MapGroup("/api/admin/display-assets").WithTags("Displays (admin)");

        assets.MapPost("/", (IFormFile file, AssetService service, CancellationToken ct) => UploadAsync(file, service, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .DisableAntiforgery()
            .AddEndpointFilter<ResultToCreatedResponseTransformer<DisplayAssetView>>();

        assets.MapGet("/", (AssetService service, CancellationToken ct) => service.ListAsync(ct))
            .RequirePermission(DisplaysPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<DisplayAssetView>>>();

        assets.MapDelete("/{assetId:guid}", (Guid assetId, AssetService service, CancellationToken ct) =>
                service.DeleteAsync(assetId, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<DisplayAssetView>>();

        // Gallery of event photos: images come from the image library above.
        var gallery = endpoints.MapGroup("/api/admin").WithTags("Gallery (admin)");

        gallery.MapGet("/events/{eventId:guid}/gallery", (Guid eventId, GalleryService service, CancellationToken ct) =>
                service.ListAsync(eventId, ct))
            .RequirePermission(DisplaysPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<GalleryPhotoView>>>();

        gallery.MapPost("/events/{eventId:guid}/gallery", (Guid eventId, AddGalleryPhotoRequest request, GalleryService service, CancellationToken ct) =>
                service.AddAsync(eventId, request, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<GalleryPhotoView>>();

        gallery.MapPut("/gallery/{photoId:guid}", (Guid photoId, ChangeGalleryPhotoRequest request, GalleryService service, CancellationToken ct) =>
                service.ChangeAsync(photoId, request, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<GalleryPhotoView>>();

        gallery.MapDelete("/gallery/{photoId:guid}", (Guid photoId, GalleryService service, CancellationToken ct) =>
                service.RemoveAsync(photoId, ct))
            .RequirePermission(DisplaysPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer>();

        endpoints.MapGet("/api/client/gallery", (GalleryService service, CancellationToken ct) => service.PublicAsync(ct))
            .WithTags("Gallery (client)")
            .AllowAnonymous()
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<GalleryEventView>>>();
    }

    private static async Task<Common.Domain.ResultMonad.Result<DisplayAssetView>> UploadAsync(IFormFile file, AssetService service, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();

        return await service.UploadAsync(file.FileName, stream, ct);
    }

    private static Guid DeviceId(HttpContext http) =>
        Guid.TryParse(http.User.FindFirst(DisplayDeviceAuthenticationHandler.DeviceIdClaim)?.Value, out var id) ? id : Guid.Empty;
}
