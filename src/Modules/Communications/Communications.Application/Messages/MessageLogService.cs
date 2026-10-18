using Common.Domain.Phones;
using Common.Domain.ResultMonad;
using Communications.Application.Abstractions;
using Communications.Domain.Common;
using Communications.Domain.Messages;
using Microsoft.Extensions.Options;

namespace Communications.Application.Messages;

/// <summary>The message log (UF-020): what was sent to whom and whether it arrived, plus delivery reports.</summary>
public sealed class MessageLogService(
    IMessageRepository messages,
    ICommunicationsUnitOfWork unitOfWork,
    IOptions<CommunicationsOptions> options,
    TimeProvider time)
{
    public const int DefaultPageSize = 50;
    public const int MaxSummaryDays = 31;

    public async Task<Result<IReadOnlyList<MessageView>>> ListAsync(
        MessageStatus? status,
        MessageChannel? channel,
        MessageKind? kind,
        Guid? broadcastId,
        string? recipient,
        DateTimeOffset? before,
        int? limit,
        CancellationToken cancellationToken)
    {
        var search = recipient?.Trim();
        if (search is { Length: > Message.RecipientMaxLength })
        {
            return CommunicationsErrors.Invalid("SearchInvalid", ["Recipient: Search with a phone number or e-mail address."]);
        }

        // People type numbers as 07…; the log stores E.164.
        if (search is { Length: > 0 } && KenyanMobile.TryCreate(search, out var mobile))
        {
            search = mobile!.Value;
        }

        var list = await messages.ListAsync(
            new MessageFilter(
                status,
                channel,
                kind,
                broadcastId is { } id ? new BroadcastId(id) : null,
                string.IsNullOrEmpty(search) ? null : search,
                before,
                Math.Clamp(limit ?? DefaultPageSize, 1, options.Value.MaxPageSize)),
            cancellationToken);

        return Result<IReadOnlyList<MessageView>>.Success([.. list.Select(MessageView.From)]);
    }

    /// <summary>Counts by status over the last <paramref name="hours" /> (default 24).</summary>
    public async Task<Result<MessageSummaryView>> SummaryAsync(int? hours, CancellationToken cancellationToken)
    {
        var window = hours ?? 24;
        if (window is < 1 or > MaxSummaryDays * 24)
        {
            return CommunicationsErrors.Invalid("SummaryInvalid", [$"Hours: Choose 1-{MaxSummaryDays * 24}."]);
        }

        var since = time.GetUtcNow().AddHours(-window);
        var counts = await messages.CountSinceAsync(since, cancellationToken);

        return Summary(since, counts);
    }

    public static MessageSummaryView Summary(DateTimeOffset since, MessageCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        var attempted = counts.Sent + counts.Delivered + counts.Failed;
        var rate = attempted == 0 ? 0m : Math.Round((decimal)counts.Failed / attempted, 4);

        return new MessageSummaryView(since, counts.Queued, counts.Sent, counts.Delivered, counts.Failed, rate);
    }

    /// <summary>
    ///     Applies a delivery report. Unknown message ids and statuses that are neither delivered nor failed (queued,
    ///     buffered, ...) are accepted and ignored, so the provider does not retry them forever.
    /// </summary>
    public async Task ApplyDeliveryReportAsync(DeliveryReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (string.IsNullOrWhiteSpace(report.ProviderMessageId) ||
            report.ProviderMessageId.Length > Message.ProviderIdMaxLength ||
            DeliveryStatus.Parse(report.Status) is not { } delivered)
        {
            return;
        }

        var message = await messages.FindByProviderIdAsync(report.ProviderMessageId.Trim(), cancellationToken);
        if (message is null || !message.ApplyDeliveryReport(delivered, report.Status, time.GetUtcNow()))
        {
            return;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            // Two reports for the same message at once: the other one won; nothing to do.
            unitOfWork.Reset();
        }
    }
}

/// <summary>Provider delivery statuses reduced to delivered / failed / unknown.</summary>
public static class DeliveryStatus
{
    private static readonly string[] Failed =
        ["fail", "reject", "undeliver", "expire", "impossible", "blacklist", "invalid", "notdelivered", "not delivered"];

    /// <returns>True delivered, false failed, <c>null</c> for an in-between or unknown status.</returns>
    public static bool? Parse(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var text = status.Trim().ToUpperInvariant();

        if (Failed.Any(x => text.Contains(x.ToUpperInvariant(), StringComparison.Ordinal)))
        {
            return false;
        }

        return text.Contains("DELIVERED", StringComparison.Ordinal) || text is "SUCCESS" or "DELIVRD" ? true : null;
    }
}
