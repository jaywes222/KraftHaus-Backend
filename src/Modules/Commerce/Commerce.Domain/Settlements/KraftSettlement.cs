using Commerce.Domain.Common;
using Common.Domain.Aggregates;
using Common.Domain.Events;
using Common.Domain.ResultMonad;

namespace Commerce.Domain.Settlements;

public enum SettlementDirection
{
    /// <summary>The platform paid the Kraft its share of money collected in the platform's till.</summary>
    PlatformPaidKraft = 1,

    /// <summary>The Kraft paid the platform its commission and service fees collected in the Kraft's own till.</summary>
    KraftPaidPlatform = 2
}

/// <summary>
///     A settlement payment recorded by Finance between the platform and a Kraft for one event (M-Pesa, bank). The
///     statement nets these against what each side owes. Records are corrected with an opposite entry, never edited.
/// </summary>
public sealed class KraftSettlement : AggregateRoot<KraftSettlementId>
{
    public const int ReferenceMaxLength = 60;
    public const int NoteMaxLength = 300;
    public const decimal MaxAmount = 10_000_000m;

    private KraftSettlement(KraftSettlementId id)
        : base(id)
    {
    }

    public Guid KraftId { get; private set; }

    public Guid EventId { get; private set; }

    public SettlementDirection Direction { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>M-Pesa or bank reference.</summary>
    public string Reference { get; private set; } = string.Empty;

    public string? Note { get; private set; }

    public DateTimeOffset PaidAt { get; private set; }

    public Guid RecordedBy { get; private set; }

    public static Result<KraftSettlement> Record(
        KraftSettlementId id,
        Guid kraftId,
        Guid eventId,
        SettlementDirection direction,
        decimal amount,
        string? reference,
        string? note,
        DateTimeOffset paidAt,
        Guid recordedBy)
    {
        var errors = new List<string>();
        var cleanReference = reference?.Trim() ?? string.Empty;
        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (!Enum.IsDefined(direction))
        {
            errors.Add("Direction: Choose PlatformPaidKraft or KraftPaidPlatform.");
        }

        if (amount is <= 0m or > MaxAmount || decimal.Round(amount, 2) != amount)
        {
            errors.Add("Amount: Enter an amount above 0 with at most two decimals.");
        }

        if (cleanReference.Length is < 3 or > ReferenceMaxLength)
        {
            errors.Add($"Reference: Give the M-Pesa or bank reference (3-{ReferenceMaxLength} characters).");
        }

        if (cleanNote is { Length: > NoteMaxLength })
        {
            errors.Add($"Note: Keep it under {NoteMaxLength} characters.");
        }

        if (errors.Count > 0)
        {
            return CommerceErrors.Invalid("SettlementInvalid", errors);
        }

        var settlement = new KraftSettlement(id)
        {
            KraftId = kraftId,
            EventId = eventId,
            Direction = direction,
            Amount = amount,
            Reference = cleanReference,
            Note = cleanNote,
            PaidAt = paidAt,
            RecordedBy = recordedBy
        };

        settlement.RaiseEvent(new KraftSettlementRecorded(id, kraftId, eventId, direction, amount));

        return settlement;
    }
}

public sealed record KraftSettlementRecorded(KraftSettlementId SettlementId, Guid KraftId, Guid EventId, SettlementDirection Direction, decimal Amount) : DomainEvent;
