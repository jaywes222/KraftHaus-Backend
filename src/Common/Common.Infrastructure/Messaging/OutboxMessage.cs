using System.Diagnostics;
using System.Text.Json;
using Common.IntegrationEvents;

namespace Common.Infrastructure.Messaging;

/// <summary>
///     One integration event waiting to be delivered (Spec 08 §9.1), written in the <b>same transaction</b> as the
///     state change that produced it. Each module keeps its own <c>&lt;schema&gt;.outbox_message</c> table in every
///     tenant database; the dispatcher drains them.
/// </summary>
public sealed class OutboxMessage
{
    public const string TableName = "outbox_message";
    public const int TypeMaxLength = 200;
    public const int ErrorMaxLength = 2000;

    private OutboxMessage()
    {
    }

    /// <summary>The integration event's id: consumers de-duplicate on it.</summary>
    public Guid Id { get; private set; }

    /// <summary>The event's type name (a key of <see cref="IntegrationEventTypes" />).</summary>
    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    /// <summary>W3C <c>traceparent</c> of the request that wrote it, so a trace spans request and handler.</summary>
    public string? TraceParent { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset AvailableAt { get; private set; }

#pragma warning disable S1144 // Written by the dispatcher in SQL and read back by EF Core.
    public int Attempts { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public DateTimeOffset? DeadLetteredAt { get; private set; }

    public string? LastError { get; private set; }
#pragma warning restore S1144

    public static OutboxMessage From(IntegrationEvent @event, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var type = @event.GetType();

        return new OutboxMessage
        {
            Id = @event.Id,
            Type = IntegrationEventTypes.NameOf(type),
            Payload = JsonSerializer.Serialize(@event, type, IntegrationEventTypes.SerializerOptions),
            TraceParent = Activity.Current?.Id,
            CreatedAt = now,
            AvailableAt = now
        };
    }
}

/// <summary>A message a consumer module has already handled (Spec 08 §9.2). The composite key makes re-delivery a no-op.</summary>
public sealed class ProcessedMessage
{
    public const string TableName = "processed_message";
    public const int HandlerMaxLength = 200;

    private ProcessedMessage()
    {
    }

    public Guid MessageId { get; private set; }

    public string Handler { get; private set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; private set; }

    public static ProcessedMessage For(Guid messageId, string handler, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handler);

        return new ProcessedMessage { MessageId = messageId, Handler = handler, ProcessedAt = now };
    }
}
