using System.Text.RegularExpressions;
using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Payments.Domain.Common;

namespace Payments.Domain.Destinations;

public enum PaymentDestinationKind
{
    /// <summary>A Kraft's own M-Pesa till or paybill for one event (D-03).</summary>
    KraftTill = 1,

    /// <summary>
    ///     The KRAFTHAUS till or paybill: ticket money and food sold through the platform till. One per tenant,
    ///     set by a Tenant Admin or Finance; no Kraft and no event (both <see cref="Guid.Empty" />).
    /// </summary>
    PlatformTill = 2
}

public enum ShortcodeType
{
    /// <summary>Lipa na M-Pesa till number (Buy Goods).</summary>
    BuyGoods = 1,

    Paybill = 2
}

public enum DestinationStatus
{
    /// <summary>Sent by the Kraft; Finance has not decided yet.</summary>
    Submitted = 1,

    /// <summary>Takes money (UF-017).</summary>
    Approved = 2,

    Rejected = 3,

    /// <summary>Was approved; no new payments go to it.</summary>
    Disabled = 4
}

public enum TillTestStatus
{
    NotTested = 1,

    /// <summary>The test prompt is on the tester's phone.</summary>
    Pending = 2,

    Passed = 3,

    Failed = 4
}

/// <summary>
///     Where a payment's money goes (Spec 08 §7.1). Today: a Kraft's till for one event. The PalPluss payment channel
///     that settles into the till is created by Finance in the PalPluss dashboard and recorded here; a test payment
///     through that channel must pass before the till can be approved (UF-017). Ticket money keeps going to the
///     platform account (D-02), which is configuration, not a row.
/// </summary>
public sealed partial class PaymentDestination : AggregateRoot<PaymentDestinationId>
{
    public const int NameMaxLength = 80;
    public const int NoteMaxLength = 500;
    public const int AccountNumberMaxLength = 20;
    public const int ShortcodeMaxLength = 7;

    private PaymentDestination(PaymentDestinationId id)
        : base(id)
    {
    }

    public PaymentDestinationKind Kind { get; private set; }

    public Guid KraftId { get; private set; }

    public Guid EventId { get; private set; }

    /// <summary>What Finance and the Kraft call it ("Mama Pima till").</summary>
    public string Name { get; private set; } = string.Empty;

    public ShortcodeType ShortcodeType { get; private set; }

    /// <summary>Till or paybill number (5-7 digits).</summary>
    public string Shortcode { get; private set; } = string.Empty;

    /// <summary>Paybill account number, when the paybill needs one.</summary>
    public string? AccountNumber { get; private set; }

    public DestinationStatus Status { get; private set; }

    /// <summary>The PalPluss payment channel that pays into this till (set by Finance before the test).</summary>
    public Guid? ProviderChannelId { get; private set; }

    public TillTestStatus TestStatus { get; private set; }

    public Guid? TestPaymentId { get; private set; }

    public DateTimeOffset? TestedAt { get; private set; }

    public Guid SubmittedBy { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public Guid? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Why it was rejected or disabled.</summary>
    public string? DecisionNote { get; private set; }

    /// <summary>Still in play: blocks a second till for the same Kraft and event (or a second platform till).</summary>
    public bool IsActive => Status is DestinationStatus.Submitted or DestinationStatus.Approved;

    public bool IsPlatform => Kind == PaymentDestinationKind.PlatformTill;

    public static Result<PaymentDestination> SubmitKraftTill(
        PaymentDestinationId id,
        Guid kraftId,
        Guid eventId,
        string? name,
        ShortcodeType shortcodeType,
        string? shortcode,
        string? accountNumber,
        Guid submittedBy,
        DateTimeOffset now)
    {
        var errors = new List<string>();

        if (kraftId == Guid.Empty || eventId == Guid.Empty)
        {
            errors.Add("EventId: Choose the event this till is for.");
        }

        return Submit(id, PaymentDestinationKind.KraftTill, kraftId, eventId, name, shortcodeType, shortcode, accountNumber, submittedBy, now, errors);
    }

    /// <summary>The KRAFTHAUS till or paybill. Tested and approved like a Kraft till before money goes to it.</summary>
    public static Result<PaymentDestination> SubmitPlatformTill(
        PaymentDestinationId id,
        string? name,
        ShortcodeType shortcodeType,
        string? shortcode,
        string? accountNumber,
        Guid submittedBy,
        DateTimeOffset now) =>
        Submit(id, PaymentDestinationKind.PlatformTill, Guid.Empty, Guid.Empty, name, shortcodeType, shortcode, accountNumber, submittedBy, now, []);

    private static Result<PaymentDestination> Submit(
        PaymentDestinationId id,
        PaymentDestinationKind kind,
        Guid kraftId,
        Guid eventId,
        string? name,
        ShortcodeType shortcodeType,
        string? shortcode,
        string? accountNumber,
        Guid submittedBy,
        DateTimeOffset now,
        List<string> errors)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length is < 2 or > NameMaxLength)
        {
            errors.Add($"Name: Use 2-{NameMaxLength} characters.");
        }

        if (!Enum.IsDefined(shortcodeType))
        {
            errors.Add("ShortcodeType: Choose BuyGoods or Paybill.");
        }

        var code = (shortcode ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!ShortcodePattern().IsMatch(code))
        {
            errors.Add("Shortcode: Enter the 5-7 digit till or paybill number.");
        }

        var account = string.IsNullOrWhiteSpace(accountNumber) ? null : accountNumber.Trim();
        if (account is not null && (shortcodeType != ShortcodeType.Paybill || !AccountPattern().IsMatch(account)))
        {
            errors.Add(shortcodeType == ShortcodeType.Paybill
                ? $"AccountNumber: Use 1-{AccountNumberMaxLength} letters or digits."
                : "AccountNumber: Only a paybill has an account number.");
        }

        if (errors.Count > 0)
        {
            return PaymentsErrors.Fields("TillInvalid", errors);
        }

        var destination = new PaymentDestination(id)
        {
            Kind = kind,
            KraftId = kraftId,
            EventId = eventId,
            Name = trimmedName,
            ShortcodeType = shortcodeType,
            Shortcode = code,
            AccountNumber = account,
            Status = DestinationStatus.Submitted,
            TestStatus = TillTestStatus.NotTested,
            SubmittedBy = submittedBy,
            SubmittedAt = now
        };

        destination.RaiseEvent(new TillSubmitted(id, kraftId, eventId));

        return destination;
    }

    /// <summary>Records the PalPluss channel and the test prompt about to be sent through it.</summary>
    public Result StartTest(Guid channelId, Guid testPaymentId, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return PaymentsErrors.InvalidState("TillNotTestable", "Only a submitted or approved till can be tested.");
        }

        if (TestStatus == TillTestStatus.Pending)
        {
            return PaymentsErrors.InvalidState("TillTestInProgress", "A test prompt is still waiting on the phone.");
        }

        if (channelId == Guid.Empty)
        {
            return PaymentsErrors.Fields("TillTestInvalid", ["ChannelId: Enter the PalPluss payment channel id for this till."]);
        }

        ProviderChannelId = channelId;
        TestPaymentId = testPaymentId;
        TestStatus = TillTestStatus.Pending;
        TestedAt = now;

        RaiseEvent(new TillTestStarted(Id, testPaymentId));

        return Result.Success;
    }

    /// <summary>The test payment finished. Ignored unless it is the latest test of this till.</summary>
    public bool RecordTestResult(Guid paymentId, bool passed, DateTimeOffset now)
    {
        if (TestPaymentId != paymentId || TestStatus != TillTestStatus.Pending)
        {
            return false;
        }

        TestStatus = passed ? TillTestStatus.Passed : TillTestStatus.Failed;
        TestedAt = now;

        RaiseEvent(new TillTestFinished(Id, paymentId, passed));

        return true;
    }

    public Result Approve(Guid decidedBy, DateTimeOffset now)
    {
        if (Status != DestinationStatus.Submitted)
        {
            return PaymentsErrors.InvalidState("TillNotSubmitted", "Only a submitted till can be approved.");
        }

        if (TestStatus != TillTestStatus.Passed || ProviderChannelId is null)
        {
            return PaymentsErrors.InvalidState("TillTestRequired", "Run a test payment through this till and see it pass before approving it.");
        }

        Status = DestinationStatus.Approved;
        DecidedBy = decidedBy;
        DecidedAt = now;
        DecisionNote = null;

        RaiseEvent(new TillApproved(Id, KraftId, EventId));

        return Result.Success;
    }

    public Result Reject(string? note, Guid decidedBy, DateTimeOffset now)
    {
        if (Status != DestinationStatus.Submitted)
        {
            return PaymentsErrors.InvalidState("TillNotSubmitted", "Only a submitted till can be rejected.");
        }

        var trimmed = CheckNote(note);
        if (trimmed.IsFailure)
        {
            return trimmed.Error!;
        }

        Status = DestinationStatus.Rejected;
        DecidedBy = decidedBy;
        DecidedAt = now;
        DecisionNote = trimmed.Value;

        RaiseEvent(new TillRejected(Id));

        return Result.Success;
    }

    public Result Disable(string? note, Guid decidedBy, DateTimeOffset now)
    {
        if (Status != DestinationStatus.Approved)
        {
            return PaymentsErrors.InvalidState("TillNotApproved", "Only an approved till can be disabled.");
        }

        var trimmed = CheckNote(note);
        if (trimmed.IsFailure)
        {
            return trimmed.Error!;
        }

        Status = DestinationStatus.Disabled;
        DecidedBy = decidedBy;
        DecidedAt = now;
        DecisionNote = trimmed.Value;

        RaiseEvent(new TillDisabled(Id));

        return Result.Success;
    }

    private static Result<string> CheckNote(string? note)
    {
        var trimmed = note?.Trim();

        return string.IsNullOrEmpty(trimmed) || trimmed.Length > NoteMaxLength
            ? PaymentsErrors.Fields("TillDecisionInvalid", [$"Note: Say why, in 1-{NoteMaxLength} characters."])
            : trimmed;
    }

    [GeneratedRegex("^[0-9]{5,7}$")]
    private static partial Regex ShortcodePattern();

    [GeneratedRegex("^[A-Za-z0-9]{1,20}$")]
    private static partial Regex AccountPattern();
}
