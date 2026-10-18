using Common.Domain.Aggregates;
using Common.Domain.Events;
using Common.Domain.ResultMonad;
using Payments.Domain.Common;

namespace Payments.Domain.Payments;

public enum SettlementStatus
{
    /// <summary>Paid by the customer, not yet in the receiving account / not yet paid out.</summary>
    Unsettled = 1,

    Settled = 2
}

/// <summary>
///     Settlement of one successful payment (BR-025): kept in its own table and never inferred from the payment's
///     status. Updated by Finance by hand (or by a settlement import later). The id is the payment's id.
/// </summary>
public sealed class SettlementRecord : AggregateRoot<PaymentId>
{
    public const int ReferenceMaxLength = 64;
    public const int NoteMaxLength = 500;

    private SettlementRecord(PaymentId id)
        : base(id)
    {
    }

    public SettlementStatus Status { get; private set; }

    /// <summary>Bank / PalPluss settlement reference.</summary>
    public string? Reference { get; private set; }

    public DateTimeOffset? SettledAt { get; private set; }

    public string? Note { get; private set; }

    public Guid RecordedBy { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public static Result<SettlementRecord> Record(
        Payment payment,
        SettlementStatus status,
        string? reference,
        DateTimeOffset? settledAt,
        string? note,
        Guid recordedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payment);

        var record = new SettlementRecord(payment.Id);
        var updated = record.Update(payment, status, reference, settledAt, note, recordedBy, now);

        return updated.IsFailure ? updated.Error! : record;
    }

    public Result Update(
        Payment payment,
        SettlementStatus status,
        string? reference,
        DateTimeOffset? settledAt,
        string? note,
        Guid recordedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payment);

        if (payment.Status != PaymentStatus.Succeeded)
        {
            return PaymentsErrors.InvalidState("NotSettleable", "Only successful payments are settled.");
        }

        if (!Enum.IsDefined(status))
        {
            return PaymentsErrors.Invalid("Status", "Choose Unsettled or Settled.");
        }

        var cleanReference = reference?.Trim();
        var cleanNote = note?.Trim();

        if (cleanReference is { Length: > ReferenceMaxLength } || cleanNote is { Length: > NoteMaxLength })
        {
            return PaymentsErrors.Invalid("Settlement", $"Reference is at most {ReferenceMaxLength} and note {NoteMaxLength} characters.");
        }

        if (status == SettlementStatus.Settled && string.IsNullOrEmpty(cleanReference))
        {
            return PaymentsErrors.Invalid("Reference", "A settled payment needs its settlement reference.");
        }

        if (settledAt > now)
        {
            return PaymentsErrors.Invalid("SettledAt", "The settlement date cannot be in the future.");
        }

        var @event = new SettlementRecorded(Id, status);

        Status = status;
        Reference = string.IsNullOrEmpty(cleanReference) ? null : cleanReference;
        SettledAt = status == SettlementStatus.Settled ? settledAt ?? now : null;
        Note = string.IsNullOrEmpty(cleanNote) ? null : cleanNote;
        RecordedBy = recordedBy;
        RecordedAt = now;

        RaiseEvent(@event);

        return Result.Success;
    }
}

public sealed record SettlementRecorded(PaymentId PaymentId, SettlementStatus Status) : DomainEvent;
