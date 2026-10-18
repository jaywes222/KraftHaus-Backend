using Displays.Domain.Devices;

namespace Displays.Application.Devices;

/// <summary>What a screen gets when it asks to be paired: show <see cref="Code" />, keep <see cref="Secret" /> to poll.</summary>
public sealed record PairingStartView(Guid PairingId, string Code, string Secret, DateTimeOffset ExpiresAt, int PollAfterSeconds);

public sealed record PollPairingRequest(string? Secret);

/// <param name="Status">Waiting, Paired (token in this response, once) or Delivered (already picked up).</param>
public sealed record PairingPollView(string Status, DateTimeOffset ExpiresAt, Guid? DeviceId, string? DeviceToken);

public sealed record PairDisplayRequest(string? Code, string? Name, string? Location, Guid EventId, Guid? DefaultConfigurationId);

public sealed record UpdateDisplayRequest(string? Name, string? Location, Guid EventId, Guid? DefaultConfigurationId);

public sealed record ScheduleEntryRequest(Guid ConfigurationId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public sealed record SetScheduleRequest(IReadOnlyList<ScheduleEntryRequest>? Entries);

public sealed record RevokeDisplayRequest(string? Reason);

public sealed record ScheduleEntryView(Guid Id, Guid ConfigurationId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <param name="Online">Seen within <c>Displays:OnlineWindowSeconds</c>.</param>
/// <param name="ShowingConfigurationId">What it shows now (schedule, else default); null shows "no content".</param>
public sealed record DisplayDeviceView(
    Guid Id,
    string Name,
    string? Location,
    Guid EventId,
    Guid? DefaultConfigurationId,
    Guid? ShowingConfigurationId,
    string Status,
    bool Online,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset PairedAt,
    DateTimeOffset? RevokedAt,
    string? RevokeReason,
    IReadOnlyList<ScheduleEntryView> Schedule)
{
    public static DisplayDeviceView From(DisplayDevice device, DateTimeOffset now, TimeSpan onlineWindow)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new DisplayDeviceView(
            device.Id.Value,
            device.Name,
            device.Location,
            device.EventId,
            device.DefaultConfigurationId,
            device.Status == DisplayDeviceStatus.Active ? device.ConfigurationAt(now) : null,
            device.Status.ToString(),
            device.IsOnline(now, onlineWindow),
            device.LastSeenAt,
            device.PairedAt,
            device.RevokedAt,
            device.RevokeReason,
            [.. device.Schedule.Select(x => new ScheduleEntryView(x.Id, x.ConfigurationId, x.StartsAt, x.EndsAt))]);
    }
}
