using Displays.Domain.Common;

namespace Displays.Domain.Devices;

public enum DisplayPairingStatus
{
    /// <summary>The screen shows the code and waits.</summary>
    Waiting = 1,

    /// <summary>Staff entered the code; the screen picks up its token on the next poll.</summary>
    Claimed = 2,

    /// <summary>The token was handed to the screen (once).</summary>
    Delivered = 3
}

/// <summary>
///     A screen asking to be paired (UF-019): it shows <see cref="Code" /> on the TV and polls with a secret only it knows.
///     Staff type the code into the admin; the next poll hands the screen its device token, once. Not an aggregate: no
///     audit trail of anonymous requests; the pairing itself is audited on the device.
/// </summary>
public sealed class DisplayPairing
{
    public const int CodeLength = 6;

    private DisplayPairing()
    {
    }

    public DisplayPairingId Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string SecretHash { get; private set; } = string.Empty;

    public DisplayPairingStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public Guid? DeviceId { get; private set; }

    /// <summary>The device token waiting for the screen, protected for the short time between claim and pick-up.</summary>
    public string? PendingToken { get; private set; }

    public static DisplayPairing Start(DisplayPairingId id, string code, string secretHash, DateTimeOffset now, TimeSpan lifetime) =>
        new()
        {
            Id = id,
            Code = code,
            SecretHash = secretHash,
            Status = DisplayPairingStatus.Waiting,
            CreatedAt = now,
            ExpiresAt = now + lifetime
        };

    public bool IsExpired(DateTimeOffset now) => Status == DisplayPairingStatus.Waiting && now >= ExpiresAt;

    public bool CanBeClaimed(DateTimeOffset now) => Status == DisplayPairingStatus.Waiting && now < ExpiresAt;

    public void Claim(Guid deviceId, string protectedToken)
    {
        Status = DisplayPairingStatus.Claimed;
        DeviceId = deviceId;
        PendingToken = protectedToken;
    }

    public void Deliver()
    {
        Status = DisplayPairingStatus.Delivered;
        PendingToken = null;
    }
}
