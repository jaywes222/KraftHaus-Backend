using Common.Application.Notifications;
using Common.Application.Tenancy;
using Communications.Application.Abstractions;
using Communications.Domain.Common;
using Communications.Domain.Messages;

namespace Communications.Application.Messages;

/// <summary>
///     <see cref="IMessageRecorder" />: a <see cref="MessageKind.Transactional" /> row per SMS / e-mail another module
///     sent, in the host tenant's database. Without a resolved tenant (nothing to write to) it does nothing.
/// </summary>
public sealed class MessageRecorder(
    ICurrentTenant tenant,
    IMessageRepository messages,
    ICommunicationsUnitOfWork unitOfWork,
    TimeProvider time)
    : IMessageRecorder
{
    public Task RecordSmsAsync(SmsMessage message, SmsReceipt? receipt, string? error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return RecordAsync(MessageChannel.Sms, message.PhoneNumber, null, message.Message, receipt?.ProviderMessageId, error, cancellationToken);
    }

    public Task RecordEmailAsync(EmailMessage message, string? error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // The e-mail body may carry a code or a link; only the subject is kept.
        return RecordAsync(MessageChannel.Email, message.To, message.Subject, string.Empty, null, error, cancellationToken);
    }

    private async Task RecordAsync(
        MessageChannel channel,
        string recipient,
        string? subject,
        string body,
        string? providerMessageId,
        string? error,
        CancellationToken cancellationToken)
    {
        if (!tenant.IsResolved || string.IsNullOrWhiteSpace(recipient))
        {
            return;
        }

        var now = time.GetUtcNow();
        messages.Add(Message.Transactional(
            new MessageId(Guid.CreateVersion7(now)),
            channel,
            recipient,
            subject,
            body,
            providerMessageId,
            error,
            now));

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            unitOfWork.Reset();
        }
    }
}
