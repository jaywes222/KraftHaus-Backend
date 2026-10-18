using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Displays.Domain.Common;

namespace Displays.Domain.Devices;

public enum DisplayDeviceStatus
{
    Active = 1,

    /// <summary>Its token no longer works; the screen must be paired again.</summary>
    Revoked = 2
}

/// <summary>A time window in which a screen shows a configuration other than its default.</summary>
public sealed record DisplayScheduleEntry(Guid Id, Guid ConfigurationId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <param name="Name">What staff call it ("Main bar TV").</param>
/// <param name="Location">Where it hangs, free text ("Next to the food court").</param>
public sealed record DisplayDeviceSpec(string? Name, string? Location, Guid EventId, Guid? DefaultConfigurationId);

/// <summary>
///     A paired screen (UF-019). It authenticates with a long-lived device token (only the token's hash is stored) that
///     staff can revoke at once. What it shows is its default configuration, unless a schedule entry covers the moment
///     ("scheduled switching by time"); with neither it shows a "no content" screen.
/// </summary>
public sealed class DisplayDevice : AggregateRoot<DisplayDeviceId>
{
    public const int NameMaxLength = 60;
    public const int LocationMaxLength = 120;
    public const int RevokeReasonMaxLength = 200;
    public const int MaxScheduleEntries = 50;
    public static readonly TimeSpan MaxScheduleEntryLength = TimeSpan.FromDays(7);

    private List<DisplayScheduleEntry> _schedule = [];

    private DisplayDevice(DisplayDeviceId id)
        : base(id)
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string? Location { get; private set; }

    public Guid EventId { get; private set; }

    public Guid? DefaultConfigurationId { get; private set; }

    public DisplayDeviceStatus Status { get; private set; }

    /// <summary>SHA-256 of the device token (hex). The token itself is shown to the screen once, at pairing.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset PairedAt { get; private set; }

    public Guid PairedBy { get; private set; }

    /// <summary>Last time the screen fetched its feed or connected; drives online/offline.</summary>
    public DateTimeOffset? LastSeenAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public string? RevokeReason { get; private set; }

    public IReadOnlyList<DisplayScheduleEntry> Schedule => _schedule;

    public static Result<DisplayDevice> Pair(
        DisplayDeviceId id,
        DisplayDeviceSpec spec,
        string tokenHash,
        Guid pairedBy,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        var device = new DisplayDevice(id)
        {
            Status = DisplayDeviceStatus.Active,
            TokenHash = tokenHash,
            PairedAt = now,
            PairedBy = pairedBy,
            LastSeenAt = now
        };

        var applied = device.Apply(spec);
        if (applied.IsFailure)
        {
            return applied.Error!;
        }

        device.RaiseEvent(new DisplayDevicePaired(id, device.EventId, device.DefaultConfigurationId));

        return device;
    }

    public Result Update(DisplayDeviceSpec spec)
    {
        if (Status == DisplayDeviceStatus.Revoked)
        {
            return RevokedError;
        }

        var eventBefore = EventId;
        var applied = Apply(spec);
        if (applied.IsFailure)
        {
            return applied;
        }

        if (eventBefore != EventId)
        {
            // A schedule belongs to the event's configurations; moving the screen clears it.
            _schedule = [];
        }

        RaiseEvent(new DisplayDeviceUpdated(Id, EventId, DefaultConfigurationId));

        return Result.Success;
    }

    /// <summary>Replaces the whole time schedule. Entries may not overlap.</summary>
    public Result SetSchedule(IReadOnlyList<(Guid ConfigurationId, DateTimeOffset StartsAt, DateTimeOffset EndsAt)> entries, Func<Guid> newId)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(newId);

        if (Status == DisplayDeviceStatus.Revoked)
        {
            return RevokedError;
        }

        var errors = new List<string>();

        if (entries.Count > MaxScheduleEntries)
        {
            errors.Add($"Entries: At most {MaxScheduleEntries} entries.");
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var (configurationId, startsAt, endsAt) = entries[i];

            if (configurationId == Guid.Empty)
            {
                errors.Add($"Entries[{i}].ConfigurationId: Choose a configuration.");
            }

            if (endsAt <= startsAt)
            {
                errors.Add($"Entries[{i}].EndsAt: Must be after the start.");
            }
            else if (endsAt - startsAt > MaxScheduleEntryLength)
            {
                errors.Add($"Entries[{i}].EndsAt: An entry can last at most {MaxScheduleEntryLength.TotalDays:0} days.");
            }
        }

        var ordered = entries.OrderBy(x => x.StartsAt).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].StartsAt < ordered[i - 1].EndsAt)
            {
                errors.Add("Entries: Two entries overlap. A screen shows one configuration at a time.");
                break;
            }
        }

        if (errors.Count > 0)
        {
            return DisplaysErrors.Invalid("ScheduleInvalid", errors);
        }

        _schedule = [.. ordered.Select(x => new DisplayScheduleEntry(newId(), x.ConfigurationId, x.StartsAt, x.EndsAt))];
        RaiseEvent(new DisplayScheduleSet(Id, _schedule.Count));

        return Result.Success;
    }

    public Result Revoke(string? reason, Guid revokedBy, DateTimeOffset now)
    {
        if (Status == DisplayDeviceStatus.Revoked)
        {
            return RevokedError;
        }

        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > RevokeReasonMaxLength)
        {
            return DisplaysErrors.Invalid("RevokeInvalid", [$"Reason: Say why, in 1-{RevokeReasonMaxLength} characters."]);
        }

        Status = DisplayDeviceStatus.Revoked;
        RevokedAt = now;
        RevokedBy = revokedBy;
        RevokeReason = trimmed;
        RaiseEvent(new DisplayDeviceRevoked(Id, trimmed));

        return Result.Success;
    }

    /// <summary>The configuration to show at <paramref name="now" />: a schedule entry covering it, else the default.</summary>
    public Guid? ConfigurationAt(DateTimeOffset now) =>
        _schedule.FirstOrDefault(x => x.StartsAt <= now && now < x.EndsAt)?.ConfigurationId ?? DefaultConfigurationId;

    /// <summary>When what the screen shows changes next because of the schedule (an entry starting or ending).</summary>
    public DateTimeOffset? NextChangeAfter(DateTimeOffset now) =>
        _schedule
            .SelectMany(x => new[] { x.StartsAt, x.EndsAt })
            .Where(x => x > now)
            .Order()
            .Select(x => (DateTimeOffset?)x)
            .FirstOrDefault();

    public bool IsOnline(DateTimeOffset now, TimeSpan window) =>
        Status == DisplayDeviceStatus.Active && LastSeenAt is { } seen && now - seen <= window;

    private static Error RevokedError =>
        DisplaysErrors.Conflict("DeviceRevoked", "This screen has been revoked. Pair it again to use it.");

    private Result Apply(DisplayDeviceSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var errors = new List<string>();
        var name = spec.Name?.Trim() ?? string.Empty;
        var location = string.IsNullOrWhiteSpace(spec.Location) ? null : spec.Location.Trim();

        if (name.Length is < 2 or > NameMaxLength)
        {
            errors.Add($"Name: Use 2-{NameMaxLength} characters.");
        }

        if (location is { Length: > LocationMaxLength })
        {
            errors.Add($"Location: Keep it under {LocationMaxLength} characters.");
        }

        if (spec.EventId == Guid.Empty)
        {
            errors.Add("EventId: Choose the event this screen is for.");
        }

        if (spec.DefaultConfigurationId == Guid.Empty)
        {
            errors.Add("DefaultConfigurationId: Choose a configuration or leave it empty.");
        }

        if (errors.Count > 0)
        {
            return DisplaysErrors.Invalid("DeviceInvalid", errors);
        }

        Name = name;
        Location = location;
        EventId = spec.EventId;
        DefaultConfigurationId = spec.DefaultConfigurationId;

        return Result.Success;
    }
}
