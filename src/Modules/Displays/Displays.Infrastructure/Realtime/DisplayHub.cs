using Displays.Application;
using Displays.Application.Abstractions;
using Displays.Domain.Common;
using Displays.Infrastructure.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Displays.Infrastructure.Realtime;

/// <summary>
///     Screens' live channel (Spec 08 §10): <c>/hubs/display</c>, device token only. Each connection joins its screen's
///     group and its event's group. Server → screen: <c>refresh</c> <c>{ reason }</c> (re-fetch the feed) and
///     <c>revoked</c> (forget the token, show the pairing screen).
/// </summary>
public sealed class DisplayHub(IDisplayDeviceRepository devices, IOptions<DisplaysOptions> options, TimeProvider time) : Hub
{
    public const string Pattern = "/hubs/display";
    public const string Refresh = "refresh";
    public const string Revoked = "revoked";

    public static string DeviceGroup(Guid deviceId) => $"display:{deviceId:D}";

    public static string EventGroup(Guid eventId) => $"display-event:{eventId:D}";

    public override async Task OnConnectedAsync()
    {
        if (!Guid.TryParse(Context.User?.FindFirst(DisplayDeviceAuthenticationHandler.DeviceIdClaim)?.Value, out var deviceId) ||
            !Guid.TryParse(Context.User?.FindFirst(DisplayDeviceAuthenticationHandler.EventIdClaim)?.Value, out var eventId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, DeviceGroup(deviceId));
        await Groups.AddToGroupAsync(Context.ConnectionId, EventGroup(eventId));
        await devices.TouchAsync(new DisplayDeviceId(deviceId), time.GetUtcNow(), TimeSpan.FromSeconds(options.Value.SeenWriteIntervalSeconds), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}

internal sealed partial class SignalRDisplayRealtime(IHubContext<DisplayHub> hub, ILogger<SignalRDisplayRealtime> logger) : IDisplayRealtime
{
    public Task DeviceChangedAsync(Guid deviceId, CancellationToken cancellationToken) =>
        SendAsync(DisplayHub.DeviceGroup(deviceId), DisplayHub.Refresh, new { reason = "device" }, cancellationToken);

    public Task EventChangedAsync(Guid eventId, string reason, CancellationToken cancellationToken) =>
        SendAsync(DisplayHub.EventGroup(eventId), DisplayHub.Refresh, new { reason }, cancellationToken);

    public Task DeviceRevokedAsync(Guid deviceId, CancellationToken cancellationToken) =>
        SendAsync(DisplayHub.DeviceGroup(deviceId), DisplayHub.Revoked, new { reason = "revoked" }, cancellationToken);

    private async Task SendAsync(string group, string method, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(group).SendAsync(method, payload, cancellationToken);
        }
#pragma warning disable CA1031 // Best effort: screens poll their feed as well.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPushFailed(logger, group, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Display push to {Group} failed.")]
    private static partial void LogPushFailed(ILogger logger, string group, Exception exception);
}
