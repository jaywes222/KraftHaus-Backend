using Common.Domain.Communications;
using Communications.Domain.Broadcasts;
using Communications.Domain.Messages;
using Communications.Domain.OptOuts;
using Communications.Domain.Templates;

namespace Communications.Application;

public sealed record MessageView(
    Guid Id,
    MessageChannel Channel,
    MessageKind Kind,
    Guid? BroadcastId,
    string Recipient,
    string? Subject,
    string Body,
    MessageStatus Status,
    string? ProviderStatus,
    string? Error,
    DateTimeOffset QueuedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? FailedAt)
{
    public static MessageView From(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new MessageView(
            message.Id.Value,
            message.Channel,
            message.Kind,
            message.BroadcastId?.Value,
            message.Recipient,
            message.Subject,
            message.Body,
            message.Status,
            message.ProviderStatus,
            message.Error,
            message.QueuedAt,
            message.SentAt,
            message.DeliveredAt,
            message.FailedAt);
    }
}

/// <param name="FailureRate">Failed ÷ (sent + delivered + failed), 0 when nothing was sent.</param>
public sealed record MessageSummaryView(DateTimeOffset Since, int Queued, int Sent, int Delivered, int Failed, decimal FailureRate);

public sealed record BroadcastView(
    Guid Id,
    string Name,
    Guid EventId,
    BroadcastAudience Audience,
    bool Marketing,
    string Body,
    int SmsParts,
    BroadcastStatus Status,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    int Recipients,
    int Skipped,
    int Sent,
    int Failed,
    DateTimeOffset CreatedOn)
{
    public static BroadcastView From(Broadcast broadcast)
    {
        ArgumentNullException.ThrowIfNull(broadcast);

        return new BroadcastView(
            broadcast.Id.Value,
            broadcast.Name,
            broadcast.EventId,
            broadcast.Audience,
            broadcast.IsMarketing,
            broadcast.Body,
            MessageBody.SmsParts(broadcast.RenderFor(null, null)),
            broadcast.Status,
            broadcast.ScheduledFor,
            broadcast.StartedAt,
            broadcast.CompletedAt,
            broadcast.CancelledAt,
            broadcast.Recipients,
            broadcast.Skipped,
            broadcast.Sent,
            broadcast.Failed,
            broadcast.CreatedOn);
    }
}

/// <param name="OptedOut">Numbers in the audience that will be skipped (marketing audiences only).</param>
public sealed record AudiencePreviewView(BroadcastAudience Audience, bool Marketing, int Total, int OptedOut, int WillReceive, string SampleText, int SmsParts);

public sealed record OptOutView(Guid Id, string Phone, OptOutSource Source, DateTimeOffset OptedOutAt)
{
    public static OptOutView From(OptOut optOut)
    {
        ArgumentNullException.ThrowIfNull(optOut);
        return new OptOutView(optOut.Id.Value, optOut.Phone, optOut.Source, optOut.OptedOutAt);
    }
}

public sealed record MessageTemplateView(Guid Id, string Name, string Body, int SmsParts, DateTimeOffset CreatedOn, DateTimeOffset? LastModifiedOn)
{
    public static MessageTemplateView From(MessageTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new MessageTemplateView(template.Id.Value, template.Name, template.Body, MessageBody.SmsParts(template.Body), template.CreatedOn, template.LastModifiedOn);
    }
}

/// <param name="Marketing">True when the customer receives marketing messages from this organiser.</param>
public sealed record MessagePreferencesView(bool Marketing);

// ---- Inputs -----------------------------------------------------------------------------------------------------

public sealed record SaveBroadcastRequest(string? Name, Guid EventId, BroadcastAudience Audience, string? Body);

public sealed record ScheduleBroadcastRequest(DateTimeOffset? SendAt);

public sealed record AddOptOutRequest(string? Phone);

public sealed record SaveTemplateRequest(string? Name, string? Body);

public sealed record MessagePreferencesRequest(bool Marketing);

/// <summary>A delivery report, whatever shape the provider posts (JSON or form), reduced to what matters.</summary>
public sealed record DeliveryReport(string? ProviderMessageId, string? Status);
