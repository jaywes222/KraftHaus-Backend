using Common.Domain.ResultMonad;
using Communications.Application.Abstractions;
using Communications.Domain.Broadcasts;
using Communications.Domain.Common;
using Communications.Domain.Messages;
using Communications.Domain.OptOuts;
using Communications.Domain.Templates;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;

namespace Communications.Infrastructure.Persistence.Repositories;

internal sealed class MessageRepository(CommunicationsDbContext db) : IMessageRepository
{
    public void Add(Message message) => db.Messages.Add(message);

    public void AddRange(IEnumerable<Message> messages) => db.Messages.AddRange(messages);

    public Task<Message?> FindByProviderIdAsync(string providerMessageId, CancellationToken cancellationToken) =>
        db.Messages
            .Where(x => x.ProviderMessageId == providerMessageId)
            .OrderByDescending(x => x.QueuedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Message>> ListAsync(MessageFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = db.Messages.AsNoTracking();

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.Channel is { } channel)
        {
            query = query.Where(x => x.Channel == channel);
        }

        if (filter.Kind is { } kind)
        {
            query = query.Where(x => x.Kind == kind);
        }

        if (filter.BroadcastId is { } broadcastId)
        {
            query = query.Where(x => x.BroadcastId == broadcastId);
        }

        if (filter.Recipient is { } recipient)
        {
            query = query.Where(x => x.Recipient == recipient);
        }

        if (filter.Before is { } before)
        {
            query = query.Where(x => x.QueuedAt < before);
        }

        return await query.OrderByDescending(x => x.QueuedAt).Take(filter.Limit).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Message>> NextQueuedAsync(BroadcastId broadcastId, int limit, CancellationToken cancellationToken) =>
        await db.Messages
            .Where(x => x.BroadcastId == broadcastId && x.Status == MessageStatus.Queued)
            .OrderBy(x => x.QueuedAt)
            .ThenBy(x => x.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<MessageCounts> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.QueuedAt >= since)
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        int Count(MessageStatus status) => rows.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

        return new MessageCounts(Count(MessageStatus.Queued), Count(MessageStatus.Sent), Count(MessageStatus.Delivered), Count(MessageStatus.Failed));
    }
}

internal sealed class BroadcastRepository(CommunicationsDbContext db) : IBroadcastRepository
{
    public void Add(Broadcast broadcast) => db.Broadcasts.Add(broadcast);

    public Task<Broadcast?> FindAsync(BroadcastId id, CancellationToken cancellationToken) =>
        db.Broadcasts.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Broadcast>> ListAsync(Guid? eventId, CancellationToken cancellationToken)
    {
        var query = db.Broadcasts.AsNoTracking();
        if (eventId is { } id)
        {
            query = query.Where(x => x.EventId == id);
        }

        return await query.OrderByDescending(x => x.CreatedOn).Take(200).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Broadcast>> ListActiveAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Broadcasts
            .Where(x => x.Status == BroadcastStatus.Sending ||
                        (x.Status == BroadcastStatus.Scheduled && x.ScheduledFor <= now))
            .ToListAsync(cancellationToken);
}

internal sealed class OptOutRepository(CommunicationsDbContext db) : IOptOutRepository
{
    /// <summary>PostgreSQL takes many parameters, but an IN list this size keeps each query small.</summary>
    private const int ChunkSize = 1000;

    public void Add(OptOut optOut) => db.OptOuts.Add(optOut);

    public Task<OptOut?> FindByPhoneAsync(string phone, CancellationToken cancellationToken) =>
        db.OptOuts.SingleOrDefaultAsync(x => x.Phone == phone, cancellationToken);

    public async Task<IReadOnlyList<OptOut>> ListActiveAsync(int limit, CancellationToken cancellationToken) =>
        await db.OptOuts.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.OptedOutAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<string>> ActiveAmongAsync(IReadOnlyCollection<string> phones, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phones);

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in phones.Distinct(StringComparer.Ordinal).Chunk(ChunkSize))
        {
            var matches = await db.OptOuts.AsNoTracking()
                .Where(x => x.IsActive && chunk.Contains(x.Phone))
                .Select(x => x.Phone)
                .ToListAsync(cancellationToken);

            found.UnionWith(matches);
        }

        return found;
    }
}

internal sealed class MessageTemplateRepository(CommunicationsDbContext db) : IMessageTemplateRepository
{
    public void Add(MessageTemplate template) => db.Templates.Add(template);

    public Task<MessageTemplate?> FindAsync(MessageTemplateId id, CancellationToken cancellationToken) =>
        db.Templates.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MessageTemplate>> ListAsync(CancellationToken cancellationToken) =>
        await db.Templates.AsNoTracking()
            .Where(x => !x.IsArchived)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
}

internal sealed class CommunicationsUnitOfWork(CommunicationsDbContext db) : ICommunicationsUnitOfWork
{
    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return CommunicationsErrors.ConcurrentUpdate;
        }
        catch (UniqueConstraintException)
        {
            // One opt-out row per phone: two taps at once.
            return CommunicationsErrors.Conflict("AlreadyExists", "That was just done. Refresh and try again.");
        }
    }

    public void Reset() => db.ChangeTracker.Clear();
}
