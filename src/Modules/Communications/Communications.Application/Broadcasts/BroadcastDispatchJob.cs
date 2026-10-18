using Common.Application.Communications;
using Common.Application.Messaging;
using Common.Application.Notifications;
using Communications.Application.Abstractions;
using Communications.Domain.Broadcasts;
using Communications.Domain.Common;
using Communications.Domain.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Communications.Application.Broadcasts;

/// <summary>
///     Sends broadcasts, per tenant, every minute (Spec 08 §9.5, the throttled queue). A due broadcast first has its
///     recipients queued as message rows (audience minus opt-outs for marketing, de-duplicated) in one save; then each
///     run sends at most <see cref="CommunicationsOptions.BatchSize" /> of them. OTPs and confirmations never pass
///     through here, so a big broadcast cannot delay them. A crash mid-batch only leaves messages queued for the next run.
/// </summary>
public sealed partial class BroadcastDispatchJob(
    IBroadcastRepository broadcasts,
    IMessageRepository messages,
    IOptOutRepository optOuts,
    ICommunicationsUnitOfWork unitOfWork,
    IAudienceDirectory audiences,
    ISmsGateway sms,
    IOptions<CommunicationsOptions> options,
    TimeProvider time,
    ILogger<BroadcastDispatchJob> logger)
    : ITenantJob
{
    public const string Name = "communications.broadcast-dispatch";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var budget = options.Value.BatchSize;
        var active = await broadcasts.ListActiveAsync(time.GetUtcNow(), cancellationToken);

        foreach (var broadcast in active.OrderBy(x => x.StartedAt ?? x.ScheduledFor))
        {
            if (broadcast.IsDue(time.GetUtcNow()) && !await StartAsync(broadcast, cancellationToken))
            {
                continue;
            }

            if (budget <= 0)
            {
                break;
            }

            budget -= await SendBatchAsync(broadcast, budget, cancellationToken);
        }
    }

    private async Task<bool> StartAsync(Broadcast broadcast, CancellationToken cancellationToken)
    {
        var members = await audiences.ListAsync(broadcast.EventId, broadcast.Audience, cancellationToken);
        var unique = members
            .Where(x => !string.IsNullOrWhiteSpace(x.Phone))
            .GroupBy(x => x.Phone, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToList();

        var optedOut = broadcast.IsMarketing
            ? await optOuts.ActiveAmongAsync([.. unique.Select(x => x.Phone)], cancellationToken)
            : new HashSet<string>(StringComparer.Ordinal);

        var now = time.GetUtcNow();
        var footer = options.Value.MarketingFooter;
        var queued = unique
            .Where(x => !optedOut.Contains(x.Phone))
            .Select(x => Message.QueuedForBroadcast(
                new MessageId(Guid.CreateVersion7(now)), broadcast.Id, x.Phone, broadcast.RenderFor(x.FirstName, footer), now))
            .ToList();

        var started = broadcast.Start(queued.Count, unique.Count - queued.Count, now);
        if (started.IsFailure)
        {
            return false;
        }

        messages.AddRange(queued);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            // Another node started it at the same moment (optimistic concurrency); it owns the queue.
            unitOfWork.Reset();
            return false;
        }

        LogStarted(logger, broadcast.Id.Value, queued.Count);
        return true;
    }

    /// <returns>Messages attempted.</returns>
    private async Task<int> SendBatchAsync(Broadcast broadcast, int limit, CancellationToken cancellationToken)
    {
        if (broadcast.Status != BroadcastStatus.Sending)
        {
            return 0;
        }

        var batch = await messages.NextQueuedAsync(broadcast.Id, limit, cancellationToken);
        var sent = 0;
        var failed = 0;

        using (MessageRecording.Suppress())
        {
            foreach (var message in batch)
            {
                try
                {
                    var receipt = await sms.SendWithReceiptAsync(new SmsMessage(message.Recipient, message.Body), cancellationToken);
                    message.MarkSent(receipt.ProviderMessageId, time.GetUtcNow());
                    sent++;
                }
                catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
                {
                    message.MarkFailed(ex.GetType().Name, time.GetUtcNow());
                    failed++;
                }
            }
        }

        broadcast.RecordBatch(sent, failed);

        // A short (or empty) batch means the queue is drained.
        if (batch.Count < limit)
        {
            broadcast.Complete(time.GetUtcNow());
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            LogBatchNotSaved(logger, broadcast.Id.Value);
            unitOfWork.Reset();
        }
        else if (broadcast.Status == BroadcastStatus.Completed)
        {
            LogCompleted(logger, broadcast.Id.Value, broadcast.Sent, broadcast.Failed);
        }

        return batch.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Broadcast {BroadcastId} started: {Recipients} messages queued.")]
    private static partial void LogStarted(ILogger logger, Guid broadcastId, int recipients);

    [LoggerMessage(Level = LogLevel.Information, Message = "Broadcast {BroadcastId} completed: {Sent} sent, {Failed} failed.")]
    private static partial void LogCompleted(ILogger logger, Guid broadcastId, int sent, int failed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Broadcast {BroadcastId}: a batch was sent but its progress could not be saved.")]
    private static partial void LogBatchNotSaved(ILogger logger, Guid broadcastId);
}
