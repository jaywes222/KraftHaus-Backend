using Common.Domain.ResultMonad;
using Communications.Domain.Broadcasts;
using Communications.Domain.Common;
using Communications.Domain.Messages;
using Communications.Domain.OptOuts;
using Communications.Domain.Templates;

namespace Communications.Application.Abstractions;

/// <param name="Recipient">Exact phone (E.164) or e-mail.</param>
public sealed record MessageFilter(
    MessageStatus? Status,
    MessageChannel? Channel,
    MessageKind? Kind,
    BroadcastId? BroadcastId,
    string? Recipient,
    DateTimeOffset? Before,
    int Limit);

public sealed record MessageCounts(int Queued, int Sent, int Delivered, int Failed);

public interface IMessageRepository
{
    void Add(Message message);

    void AddRange(IEnumerable<Message> messages);

    Task<Message?> FindByProviderIdAsync(string providerMessageId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Message>> ListAsync(MessageFilter filter, CancellationToken cancellationToken);

    /// <summary>The oldest queued messages of a broadcast, tracked, for one dispatcher batch.</summary>
    Task<IReadOnlyList<Message>> NextQueuedAsync(BroadcastId broadcastId, int limit, CancellationToken cancellationToken);

    /// <summary>Messages created at or after <paramref name="since" />, by status (all channels and kinds).</summary>
    Task<MessageCounts> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

public interface IBroadcastRepository
{
    void Add(Broadcast broadcast);

    Task<Broadcast?> FindAsync(BroadcastId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Broadcast>> ListAsync(Guid? eventId, CancellationToken cancellationToken);

    /// <summary>Scheduled broadcasts whose time has come, and broadcasts still sending (tracked).</summary>
    Task<IReadOnlyList<Broadcast>> ListActiveAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IOptOutRepository
{
    void Add(OptOut optOut);

    Task<OptOut?> FindByPhoneAsync(string phone, CancellationToken cancellationToken);

    Task<IReadOnlyList<OptOut>> ListActiveAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Of <paramref name="phones" />, those currently opted out.</summary>
    Task<IReadOnlySet<string>> ActiveAmongAsync(IReadOnlyCollection<string> phones, CancellationToken cancellationToken);
}

public interface IMessageTemplateRepository
{
    void Add(MessageTemplate template);

    Task<MessageTemplate?> FindAsync(MessageTemplateId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<MessageTemplate>> ListAsync(CancellationToken cancellationToken);
}

public interface ICommunicationsUnitOfWork
{
    /// <summary>Saves. A lost optimistic-concurrency race comes back as <see cref="CommunicationsErrors.ConcurrentUpdate" />.</summary>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);

    void Reset();
}
