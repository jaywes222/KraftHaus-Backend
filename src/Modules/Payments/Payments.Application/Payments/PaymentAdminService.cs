using Common.Application.Messaging;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Payments.Application.Abstractions;
using Payments.Domain.Common;
using Payments.Domain.Payments;

namespace Payments.Application.Payments;

/// <summary>
///     Finance view of payments (UF-018): list, detail, "check now" against PalPluss, review of mismatched amounts,
///     settlement records (BR-025) and totals. Lists never show full phone numbers.
/// </summary>
public sealed class PaymentAdminService(
    IPaymentRepository payments,
    ISettlementRepository settlements,
    IPaymentsUnitOfWork unitOfWork,
    PaymentStatusSync sync,
    IOutboxSignal outboxSignal,
    TimeProvider time)
{
    public const int MaxPageSize = 200;

    public async Task<Result<IReadOnlyList<PaymentAdminView>>> ListAsync(
        PaymentStatus? status,
        Guid? referenceId,
        DateTimeOffset? before,
        int? limit,
        CancellationToken cancellationToken)
    {
        var page = Math.Clamp(limit ?? 50, 1, MaxPageSize);
        var list = await payments.ListForReadAsync(status, referenceId, before, page, cancellationToken);
        var settled = await settlements.FindManyAsync([.. list.Select(x => x.Id)], cancellationToken);

        return Result<IReadOnlyList<PaymentAdminView>>.Success([
            .. list.Select(x => PaymentAdminView.From(x, settled.GetValueOrDefault(x.Id)))
        ]);
    }

    public async Task<Result<PaymentAdminView>> GetAsync(PaymentId id, CancellationToken cancellationToken)
    {
        var payment = await payments.FindAsync(id, cancellationToken);
        return payment is null
            ? PaymentsErrors.NotFound(id.Value)
            : PaymentAdminView.From(payment, await settlements.FindAsync(id, cancellationToken));
    }

    public async Task<Result<PaymentAdminView>> CheckNowAsync(PaymentId id, CancellationToken cancellationToken)
    {
        var payment = await payments.FindAsync(id, cancellationToken);
        if (payment is null)
        {
            return PaymentsErrors.NotFound(id.Value);
        }

        return await sync.SyncAsync(payment, cancellationToken)
            ? PaymentAdminView.From(payment, await settlements.FindAsync(id, cancellationToken))
            : new Error
            {
                Key = "Payments.ProviderUnavailable",
                Description = "PalPluss could not be reached. Try again shortly.",
                StatusCode = System.Net.HttpStatusCode.ServiceUnavailable
            };
    }

    /// <summary>Accept a mismatched amount (the order is fulfilled) or record that it was refunded (the order can be paid again).</summary>
    public async Task<Result<PaymentAdminView>> ReviewAsync(
        PaymentId id,
        ReviewPaymentRequest request,
        ApplicationUserId reviewer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payment = await payments.FindAsync(id, cancellationToken);
        if (payment is null)
        {
            return PaymentsErrors.NotFound(id.Value);
        }

        var reviewed = payment.Review(request.Decision, request.Note, reviewer.Value, time.GetUtcNow());
        if (reviewed.IsFailure)
        {
            return reviewed.Error!;
        }

        var published = PaymentEventPublisher.Publish(unitOfWork, payment, reviewed.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (published)
        {
            outboxSignal.Notify();
        }

        return PaymentAdminView.From(payment, null);
    }

    public async Task<Result<PaymentAdminView>> RecordSettlementAsync(
        PaymentId id,
        RecordSettlementRequest request,
        ApplicationUserId recordedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payment = await payments.FindAsync(id, cancellationToken);
        if (payment is null)
        {
            return PaymentsErrors.NotFound(id.Value);
        }

        var now = time.GetUtcNow();
        var record = await settlements.FindAsync(id, cancellationToken);

        if (record is null)
        {
            var created = SettlementRecord.Record(payment, request.Status, request.Reference, request.SettledAt, request.Note, recordedBy.Value, now);
            if (created.IsFailure)
            {
                return created.Error!;
            }

            record = created.Value!;
            settlements.Add(record);
        }
        else
        {
            var updated = record.Update(payment, request.Status, request.Reference, request.SettledAt, request.Note, recordedBy.Value, now);
            if (updated.IsFailure)
            {
                return updated.Error!;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PaymentAdminView.From(payment, record);
    }

    /// <summary>Totals for a period (default: the last 30 days), for the Finance dashboard.</summary>
    public async Task<Result<PaymentSummaryView>> SummaryAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var end = to ?? time.GetUtcNow();
        var start = from ?? end.AddDays(-30);

        if (start >= end || end - start > TimeSpan.FromDays(400))
        {
            return PaymentsErrors.Invalid("Period", "Choose a period of at most 400 days with from before to.");
        }

        var totals = await payments.SummarizeAsync(start, end, cancellationToken);
        var succeeded = totals.ByStatus.FirstOrDefault(x => x.Status == PaymentStatus.Succeeded);

        return new PaymentSummaryView(
            start,
            end,
            [.. totals.ByStatus.Select(x => new PaymentStatusTotalView(x.Status.ToString(), x.Count, x.Amount))],
            succeeded?.Amount ?? 0m,
            totals.SettledAmount,
            (succeeded?.Amount ?? 0m) - totals.SettledAmount);
    }
}

public sealed record ReviewPaymentRequest(PaymentReviewDecision Decision, string? Note);

/// <param name="SettledAt">When the money settled (defaults to now when Settled).</param>
public sealed record RecordSettlementRequest(SettlementStatus Status, string? Reference, DateTimeOffset? SettledAt, string? Note);

public sealed record PaymentStatusTotalView(string Status, int Count, decimal Amount);

public sealed record PaymentSummaryView(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<PaymentStatusTotalView> ByStatus,
    decimal Collected,
    decimal Settled,
    decimal AwaitingSettlement);

/// <param name="PhoneLast3">Last three digits only; Finance matches the full number in PalPluss when needed.</param>
public sealed record PaymentAdminView(
    Guid Id,
    string Purpose,
    Guid ReferenceId,
    decimal Amount,
    string Currency,
    string Status,
    string AccountReference,
    string PhoneLast3,
    string? ProviderTransactionId,
    string? MpesaReceipt,
    string? ResultCode,
    string? ResultDescription,
    decimal? ReportedAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? LastCheckedAt,
    string? ReviewDecision,
    string? ReviewNote,
    DateTimeOffset? ReviewedAt,
    string Settlement,
    string? SettlementReference,
    DateTimeOffset? SettledAt)
{
    public static PaymentAdminView From(Payment payment, SettlementRecord? settlement)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return new PaymentAdminView(
            payment.Id.Value,
            payment.Purpose.ToString(),
            payment.ReferenceId,
            payment.Amount,
            payment.Currency,
            payment.Status.ToString(),
            payment.AccountReference,
            payment.Phone[^3..],
            payment.ProviderTransactionId,
            payment.MpesaReceipt,
            payment.ResultCode,
            payment.ResultDescription,
            payment.ReportedAmount,
            payment.CreatedAt,
            payment.CompletedAt,
            payment.LastCheckedAt,
            payment.ReviewDecision?.ToString(),
            payment.ReviewNote,
            payment.ReviewedAt,
            payment.Status != PaymentStatus.Succeeded
                ? "NotApplicable"
                : (settlement?.Status ?? SettlementStatus.Unsettled).ToString(),
            settlement?.Reference,
            settlement?.SettledAt);
    }
}
