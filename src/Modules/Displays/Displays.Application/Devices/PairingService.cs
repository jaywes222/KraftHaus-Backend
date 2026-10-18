using Common.Application.EventCatalog;
using Common.Domain.Codes;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Displays.Application.Abstractions;
using Displays.Domain.Common;
using Displays.Domain.Configurations;
using Displays.Domain.Devices;
using Microsoft.Extensions.Options;

namespace Displays.Application.Devices;

/// <summary>
///     Pairing a screen (UF-019): the TV asks for a code and shows it; staff enter it in the admin with a name and an
///     event; the TV's next poll returns its device token, once. The code is short to type; the poll is protected by a
///     long secret only the TV knows, so guessing a code never yields a token.
/// </summary>
public sealed class PairingService(
    IDisplayPairingRepository pairings,
    IDisplayDeviceRepository devices,
    IDisplayConfigurationRepository configurations,
    IDisplaysUnitOfWork unitOfWork,
    IPairingTokenProtector protector,
    IEventCatalog events,
    IOptions<DisplaysOptions> options,
    TimeProvider time)
{
    private const int CodeAttempts = 8;
    private const int PollAfterSeconds = 3;

    public async Task<Result<PairingStartView>> StartAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var lifetime = TimeSpan.FromMinutes(options.Value.PairingCodeMinutes);

        await pairings.DeleteFinishedAsync(now.AddHours(-1), cancellationToken);

        for (var attempt = 0; attempt < CodeAttempts; attempt++)
        {
            var code = ShortCode.New(DisplayPairing.CodeLength);
            if (await pairings.CodeInUseAsync(code, now, cancellationToken))
            {
                continue;
            }

            var secret = DeviceTokens.NewSecret();
            var pairing = DisplayPairing.Start(new DisplayPairingId(Guid.CreateVersion7(now)), code, DeviceTokens.Hash(secret), now, lifetime);
            pairings.Add(pairing);

            var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
            if (saved.IsFailure)
            {
                continue;
            }

            return new PairingStartView(pairing.Id.Value, code, secret, pairing.ExpiresAt, PollAfterSeconds);
        }

        return DisplaysErrors.Conflict("PairingBusy", "Could not create a pairing code. Try again.");
    }

    public async Task<Result<PairingPollView>> PollAsync(Guid pairingId, PollPairingRequest? request, CancellationToken cancellationToken)
    {
        var pairing = await pairings.FindAsync(new DisplayPairingId(pairingId), cancellationToken);
        if (pairing is null || !DeviceTokens.Matches(request?.Secret, pairing.SecretHash))
        {
            return PairingNotFound;
        }

        var now = time.GetUtcNow();

        switch (pairing.Status)
        {
            case DisplayPairingStatus.Waiting when pairing.IsExpired(now):
                return DisplaysErrors.Conflict("PairingExpired", "This code has expired. Ask for a new one.");

            case DisplayPairingStatus.Waiting:
                return new PairingPollView(nameof(DisplayPairingStatus.Waiting), pairing.ExpiresAt, null, null);

            case DisplayPairingStatus.Claimed:
                var token = pairing.PendingToken is { } protectedToken ? protector.Unprotect(protectedToken) : null;
                pairing.Deliver();

                var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
                if (saved.IsFailure)
                {
                    return saved.Error!;
                }

                return token is null
                    ? DisplaysErrors.Conflict("PairingExpired", "This code has expired. Ask for a new one.")
                    : new PairingPollView("Paired", pairing.ExpiresAt, pairing.DeviceId, token);

            default:
                return new PairingPollView(nameof(DisplayPairingStatus.Delivered), pairing.ExpiresAt, pairing.DeviceId, null);
        }
    }

    public async Task<Result<DisplayDeviceView>> ClaimAsync(PairDisplayRequest request, ApplicationUserId pairedBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = time.GetUtcNow();
        var code = ShortCode.Normalize(request.Code);
        var pairing = code.Length == DisplayPairing.CodeLength
            ? await pairings.FindWaitingByCodeAsync(code, now, cancellationToken)
            : null;

        if (pairing is null || !pairing.CanBeClaimed(now))
        {
            return DisplaysErrors.NotFound("PairingCodeNotFound", "No screen is showing that code. Check the code, or refresh the screen for a new one.");
        }

        var checkedContent = await CheckEventAndConfigurationAsync(request.EventId, request.DefaultConfigurationId, configurations, events, cancellationToken);
        if (checkedContent.IsFailure)
        {
            return checkedContent.Error!;
        }

        var token = DeviceTokens.New();
        var paired = DisplayDevice.Pair(
            new DisplayDeviceId(Guid.CreateVersion7(now)),
            new DisplayDeviceSpec(request.Name, request.Location, request.EventId, request.DefaultConfigurationId),
            DeviceTokens.Hash(token),
            pairedBy.Value,
            now);
        if (paired.IsFailure)
        {
            return paired.Error!;
        }

        var device = paired.Value!;
        devices.Add(device);
        pairing.Claim(device.Id.Value, protector.Protect(token));

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        return DisplayDeviceView.From(device, now, TimeSpan.FromSeconds(options.Value.OnlineWindowSeconds));
    }

    /// <summary>The event exists (not a draft) and, when given, the configuration is active and belongs to it.</summary>
    internal static async Task<Result> CheckEventAndConfigurationAsync(
        Guid eventId,
        Guid? configurationId,
        IDisplayConfigurationRepository configurations,
        IEventCatalog events,
        CancellationToken cancellationToken)
    {
        if (eventId == Guid.Empty || await events.GetSaleSnapshotAsync(eventId, cancellationToken) is null)
        {
            return DisplaysErrors.EventNotFound;
        }

        if (configurationId is not { } id || id == Guid.Empty)
        {
            return Result.Success;
        }

        var configuration = await configurations.FindForReadAsync(new DisplayConfigurationId(id), cancellationToken);

        return configuration is null || configuration.EventId != eventId || configuration.Status != DisplayConfigurationStatus.Active
            ? DisplaysErrors.Invalid("ConfigurationNotUsable", ["DefaultConfigurationId: Choose an active configuration of the same event."])
            : Result.Success;
    }

    private static Error PairingNotFound =>
        DisplaysErrors.NotFound("PairingNotFound", "This pairing request was not found. Ask for a new code.");
}
