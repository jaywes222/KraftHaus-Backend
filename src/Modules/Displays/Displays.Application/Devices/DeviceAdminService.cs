using Common.Application.EventCatalog;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Displays.Application.Abstractions;
using Displays.Domain.Common;
using Displays.Domain.Configurations;
using Displays.Domain.Devices;
using Microsoft.Extensions.Options;

namespace Displays.Application.Devices;

/// <summary>Screens in the admin (UF-019): status, naming, the event and default configuration, time schedule, revoke.</summary>
public sealed class DeviceAdminService(
    IDisplayDeviceRepository devices,
    IDisplayConfigurationRepository configurations,
    IDisplaysUnitOfWork unitOfWork,
    IDisplayRealtime realtime,
    IDeviceTokenCache tokenCache,
    IEventCatalog events,
    IOptions<DisplaysOptions> options,
    TimeProvider time)
{
    private TimeSpan OnlineWindow => TimeSpan.FromSeconds(options.Value.OnlineWindowSeconds);

    public async Task<Result<IReadOnlyList<DisplayDeviceView>>> ListAsync(Guid? eventId, DisplayDeviceStatus? status, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var list = await devices.ListAsync(eventId, status, cancellationToken);

        return Result<IReadOnlyList<DisplayDeviceView>>.Success([.. list.Select(x => DisplayDeviceView.From(x, now, OnlineWindow))]);
    }

    public async Task<Result<DisplayDeviceView>> GetAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var device = await devices.FindForReadAsync(new DisplayDeviceId(deviceId), cancellationToken);

        return device is null ? DisplaysErrors.DeviceNotFound : DisplayDeviceView.From(device, time.GetUtcNow(), OnlineWindow);
    }

    public async Task<Result<DisplayDeviceView>> UpdateAsync(Guid deviceId, UpdateDisplayRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var device = await devices.FindAsync(new DisplayDeviceId(deviceId), cancellationToken);
        if (device is null)
        {
            return DisplaysErrors.DeviceNotFound;
        }

        var checkedContent = await PairingService.CheckEventAndConfigurationAsync(request.EventId, request.DefaultConfigurationId, configurations, events, cancellationToken);
        if (checkedContent.IsFailure)
        {
            return checkedContent.Error!;
        }

        return await SaveAsync(device, device.Update(new DisplayDeviceSpec(request.Name, request.Location, request.EventId, request.DefaultConfigurationId)), cancellationToken);
    }

    public async Task<Result<DisplayDeviceView>> SetScheduleAsync(Guid deviceId, SetScheduleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var device = await devices.FindAsync(new DisplayDeviceId(deviceId), cancellationToken);
        if (device is null)
        {
            return DisplaysErrors.DeviceNotFound;
        }

        var entries = request.Entries ?? [];
        var usable = (await configurations.ListAsync(device.EventId, includeArchived: false, cancellationToken)).Select(x => x.Id.Value).ToHashSet();
        var unknown = entries
            .Select((x, i) => (x.ConfigurationId, Index: i))
            .Where(x => !usable.Contains(x.ConfigurationId))
            .Select(x => $"Entries[{x.Index}].ConfigurationId: Choose an active configuration of this screen's event.")
            .ToList();
        if (unknown.Count > 0)
        {
            return DisplaysErrors.Invalid("ScheduleInvalid", unknown);
        }

        var now = time.GetUtcNow();
        var set = device.SetSchedule([.. entries.Select(x => (x.ConfigurationId, x.StartsAt, x.EndsAt))], () => Guid.CreateVersion7(now));

        return await SaveAsync(device, set, cancellationToken);
    }

    public async Task<Result<DisplayDeviceView>> RevokeAsync(Guid deviceId, RevokeDisplayRequest request, ApplicationUserId revokedBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var device = await devices.FindAsync(new DisplayDeviceId(deviceId), cancellationToken);
        if (device is null)
        {
            return DisplaysErrors.DeviceNotFound;
        }

        var revoked = device.Revoke(request.Reason, revokedBy.Value, time.GetUtcNow());
        if (revoked.IsFailure)
        {
            return revoked.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        tokenCache.Evict(device.TokenHash);
        await realtime.DeviceRevokedAsync(device.Id.Value, cancellationToken);

        return DisplayDeviceView.From(device, time.GetUtcNow(), OnlineWindow);
    }

    private async Task<Result<DisplayDeviceView>> SaveAsync(DisplayDevice device, Result change, CancellationToken cancellationToken)
    {
        if (change.IsFailure)
        {
            return change.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        await realtime.DeviceChangedAsync(device.Id.Value, cancellationToken);

        return DisplayDeviceView.From(device, time.GetUtcNow(), OnlineWindow);
    }
}
