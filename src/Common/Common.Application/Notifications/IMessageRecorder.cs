namespace Common.Application.Notifications;

/// <summary>
///     Keeps a row per transactional SMS and e-mail so delivery is visible per message (UF-020). Implemented by
///     Communications; the notification gateways call it after every send, best effort. Bodies are stored with codes
///     masked (OTPs, ticket codes) and never logged.
/// </summary>
public interface IMessageRecorder
{
    Task RecordSmsAsync(SmsMessage message, SmsReceipt? receipt, string? error, CancellationToken cancellationToken);

    Task RecordEmailAsync(EmailMessage message, string? error, CancellationToken cancellationToken);
}

/// <summary>
///     Lets a sender that keeps its own message rows (Communications broadcasts) turn the gateway's automatic
///     recording off for the current async flow, so a message is never recorded twice.
/// </summary>
public static class MessageRecording
{
    private static readonly AsyncLocal<bool> Suppressed = new();

    public static bool IsSuppressed => Suppressed.Value;

    public static IDisposable Suppress()
    {
        var previous = Suppressed.Value;
        Suppressed.Value = true;
        return new Restore(previous);
    }

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => Suppressed.Value = previous;
    }
}
