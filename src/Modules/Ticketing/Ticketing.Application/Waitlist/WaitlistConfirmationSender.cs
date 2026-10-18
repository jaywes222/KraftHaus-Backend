using Common.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ticketing.Domain.Waitlist;

namespace Ticketing.Application.Waitlist;

/// <summary>Sends the "you're on the list" SMS for a new waitlist entry.</summary>
public interface IWaitlistConfirmationSender
{
    /// <summary>
    ///     <c>true</c> when the gateway accepted the message within the configured timeout. Never throws for a
    ///     provider failure: the entry is already saved and the visitor must still see "you're in".
    /// </summary>
    Task<bool> SendAsync(WaitlistEntry entry, string eventTitle, CancellationToken cancellationToken);
}

/// <summary>
///     Direct send through <see cref="ISmsGateway" />. Interim until the PostgreSQL outbox and the Communications
///     module exist (Spec 08 §9): entries whose <see cref="WaitlistEntry.ConfirmationSentAt" /> is still empty are
///     the ones a resend job must pick up.
/// </summary>
public sealed partial class WaitlistConfirmationSender(
    ISmsGateway smsGateway,
    IOptions<WaitlistOptions> options,
    ILogger<WaitlistConfirmationSender> logger)
    : IWaitlistConfirmationSender
{
    public async Task<bool> SendAsync(WaitlistEntry entry, string eventTitle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var text =
            $"You're on the waitlist for {eventTitle}. We'll text this number when early access opens. - KraftHaus";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ConfirmationSmsTimeoutSeconds));

        try
        {
            await smsGateway.SendAsync(new SmsMessage(entry.Phone, text), timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogConfirmationTimedOut(logger, entry.Id.Value);
            return false;
        }
#pragma warning disable CA1031 // A provider failure must not fail the join; the entry stays pending for a resend.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogConfirmationFailed(logger, entry.Id.Value, ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Waitlist confirmation SMS for entry {EntryId} timed out.")]
    private static partial void LogConfirmationTimedOut(ILogger logger, Guid entryId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Waitlist confirmation SMS for entry {EntryId} failed.")]
    private static partial void LogConfirmationFailed(ILogger logger, Guid entryId, Exception exception);
}
