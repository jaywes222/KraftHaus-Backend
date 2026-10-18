using Common.Application.EventCatalog;
using Common.Application.Messaging;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions;

namespace Ticketing.Application.Waitlist;

/// <summary>
///     Resends waitlist confirmations the SMS gateway did not accept at join time (provider outage, timeout), up to
///     <c>MaxConfirmationAttempts</c> and within <c>ResendWindowDays</c> of joining.
/// </summary>
public sealed class WaitlistResendJob(
    IWaitlistRepository waitlist,
    IEventCatalog events,
    IWaitlistConfirmationSender confirmation,
    ITicketingUnitOfWork unitOfWork,
    IOptions<WaitlistOptions> options,
    TimeProvider time)
    : ITenantJob
{
    public const string Name = "ticketing.waitlist-resend";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var titles = new Dictionary<Guid, string?>();

        var pending = await waitlist.ListUnconfirmedAsync(
            joinedBefore: now.AddMinutes(-settings.ResendAfterMinutes),
            joinedAfter: now.AddDays(-settings.ResendWindowDays),
            maxAttempts: settings.MaxConfirmationAttempts,
            limit: settings.ResendBatchSize,
            cancellationToken);

        foreach (var entry in pending)
        {
            if (!titles.TryGetValue(entry.EventId, out var title))
            {
                title = (await events.GetSaleSnapshotAsync(entry.EventId, cancellationToken))?.Title;
                titles[entry.EventId] = title;
            }

            if (title is null)
            {
                continue;
            }

            if (await confirmation.SendAsync(entry, title, cancellationToken))
            {
                entry.MarkConfirmationSent(time.GetUtcNow());
            }
            else
            {
                entry.RecordConfirmationFailed();
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
