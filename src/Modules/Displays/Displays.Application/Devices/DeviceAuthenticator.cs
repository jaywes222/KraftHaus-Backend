using Displays.Application.Abstractions;

namespace Displays.Application.Devices;

/// <summary>A screen as the API sees it after its token checked out.</summary>
public sealed record DeviceIdentity(Guid DeviceId, Guid EventId);

/// <summary>Turns a presented device token into the screen it belongs to (active screens only).</summary>
public sealed class DeviceAuthenticator(IDisplayDeviceRepository devices)
{
    public async Task<DeviceIdentity?> AuthenticateAsync(string? token, CancellationToken cancellationToken)
    {
        if (!DeviceTokens.LooksLikeToken(token))
        {
            return null;
        }

        var device = await devices.FindActiveByTokenHashAsync(DeviceTokens.Hash(token!), cancellationToken);

        return device is null ? null : new DeviceIdentity(device.Id.Value, device.EventId);
    }
}
