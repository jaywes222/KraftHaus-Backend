using Common.Domain.Entities;
using Communications.Domain.Common;

namespace Communications.Domain.Messages;

public enum MessageChannel
{
    Sms = 1,
    Email = 2
}

public enum MessageKind
{
    /// <summary>OTPs, confirmations, receipts, reminders: sent straight away by the module that needs them.</summary>
    Transactional = 1,

    /// <summary>One recipient of a broadcast: queued, then sent by the throttled dispatcher.</summary>
    Broadcast = 2
}

/// <summary><c>Queued → Sent → Delivered | Failed</c>; <c>Queued → Failed</c> when the provider refuses it.</summary>
public enum MessageStatus
{
    Queued = 1,
    Sent = 2,
    Delivered = 3,
    Failed = 4
}

/// <summary>
///     One SMS or e-mail to one recipient, so delivery is visible per message (UF-020, Spec 08 §9.5). Not an
///     aggregate: a log row with a small state machine, no domain events and no audit (broadcasts are audited).
/// </summary>
public sealed class Message : AuditableEntity<MessageId>
{
    public const int RecipientMaxLength = 254;
    public const int SubjectMaxLength = 200;
    public const int ProviderIdMaxLength = 128;
    public const int ProviderStatusMaxLength = 64;
    public const int ErrorMaxLength = 200;

    private Message(MessageId id)
        : base(id)
    {
    }

    public MessageChannel Channel { get; private set; }

    public MessageKind Kind { get; private set; }

    public BroadcastId? BroadcastId { get; private set; }

    /// <summary>E.164 phone or e-mail address.</summary>
    public string Recipient { get; private set; } = string.Empty;

    public string? Subject { get; private set; }

    /// <summary>Masked (<see cref="MessageBody.Mask" />): never holds a code.</summary>
    public string Body { get; private set; } = string.Empty;

    public MessageStatus Status { get; private set; }

    public string? ProviderMessageId { get; private set; }

    /// <summary>The provider's own words for the last delivery report (e.g. "DeliveredToTerminal").</summary>
    public string? ProviderStatus { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset QueuedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    /// <summary>A transactional send that already happened: <c>Sent</c> when the provider took it, else <c>Failed</c>.</summary>
    public static Message Transactional(
        MessageId id,
        MessageChannel channel,
        string recipient,
        string? subject,
        string body,
        string? providerMessageId,
        string? error,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);

        var message = new Message(id)
        {
            Channel = channel,
            Kind = MessageKind.Transactional,
            Recipient = Fit(recipient.Trim(), RecipientMaxLength)!,
            Subject = Fit(subject, SubjectMaxLength),
            Body = MessageBody.Mask(body),
            QueuedAt = now,
            Status = MessageStatus.Queued
        };

        if (error is null)
        {
            message.MarkSent(providerMessageId, now);
        }
        else
        {
            message.MarkFailed(error, now);
        }

        return message;
    }

    /// <summary>One broadcast recipient waiting for the dispatcher. The rendered body is kept as sent.</summary>
    public static Message QueuedForBroadcast(MessageId id, BroadcastId broadcastId, string phone, string body, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phone);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new Message(id)
        {
            Channel = MessageChannel.Sms,
            Kind = MessageKind.Broadcast,
            BroadcastId = broadcastId,
            Recipient = phone,
            Body = body.Length <= MessageBody.StoredMaxLength ? body : body[..MessageBody.StoredMaxLength],
            Status = MessageStatus.Queued,
            QueuedAt = now
        };
    }

    public void MarkSent(string? providerMessageId, DateTimeOffset now)
    {
        if (Status != MessageStatus.Queued)
        {
            return;
        }

        Status = MessageStatus.Sent;
        ProviderMessageId = Fit(providerMessageId, ProviderIdMaxLength);
        SentAt = now;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        if (Status is MessageStatus.Delivered or MessageStatus.Failed)
        {
            return;
        }

        Status = MessageStatus.Failed;
        Error = Fit(error, ErrorMaxLength) ?? "Failed";
        FailedAt = now;
    }

    /// <summary>
    ///     A delivery report from the provider. Idempotent and order-tolerant: <c>Delivered</c> is final, a late
    ///     "failed" after "delivered" changes nothing, and a report for a message never sent is ignored.
    /// </summary>
    /// <returns>True when the row changed.</returns>
    public bool ApplyDeliveryReport(bool delivered, string? providerStatus, DateTimeOffset now)
    {
        if (Status is MessageStatus.Queued or MessageStatus.Delivered)
        {
            return false;
        }

        if (delivered)
        {
            Status = MessageStatus.Delivered;
            DeliveredAt = now;
            Error = null;
        }
        else if (Status == MessageStatus.Sent)
        {
            Status = MessageStatus.Failed;
            FailedAt = now;
            Error = "Not delivered";
        }
        else
        {
            return false;
        }

        ProviderStatus = Fit(providerStatus, ProviderStatusMaxLength);
        return true;
    }

    private static string? Fit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
