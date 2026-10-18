using Common.Domain.Aggregates;
using Common.Domain.Events;
using Ticketing.Domain.Common;
using Common.Domain.Codes;
using Common.Domain.TimeProvision;

namespace Ticketing.Domain.Tickets;

/// <summary>
///     <c>Valid → CheckedIn</c>; <c>AwaitingBalance → Valid</c> when a Lipa Mdogo Mdogo plan is fully paid (no QR
///     until then, BR-010); <c>Void</c> when cancelled. A transfer rebinds the holder and keeps the status.
/// </summary>
public enum TicketStatus
{
    /// <summary>Reserved under a Lipa Mdogo Mdogo plan that still has a balance to pay.</summary>
    AwaitingBalance = 1,
    Valid = 2,
    CheckedIn = 3,
    Void = 4
}

public enum CheckInMethod
{
    Qr = 1,
    Manual = 2
}

/// <summary>
///     One admission to one event date (Spec 08 §8). Owned by a phone number: "my tickets" are the tickets held by
///     the signed-in account's phone, so a transfer to someone without an account is just a new holder phone.
/// </summary>
public sealed class Ticket : AggregateRoot<TicketId>
{
    public const int CodeLength = 10;
    public const int NameMaxLength = 160;

    private Ticket(TicketId id)
        : base(id)
    {
    }

    public OrderId OrderId { get; private set; }

    public OrderLineId OrderLineId { get; private set; }

    public Guid EventId { get; private set; }

    public Guid EventDateId { get; private set; }

    public Guid TierId { get; private set; }

    /// <summary>Tickets bought together as one Couple / Gang bundle share this id.</summary>
    public Guid? BundleId { get; private set; }

    /// <summary>Short code for manual check-in when the QR will not scan.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>E.164.</summary>
    public string HolderPhone { get; private set; } = string.Empty;

    public string HolderName { get; private set; } = string.Empty;

    /// <summary>
    ///     Who will use the ticket, when the holder buys for someone else but keeps the ticket (shown at the door).
    ///     Unlike a transfer it does not move the ticket or count against the transfer allowance.
    /// </summary>
    public string? AttendeeName { get; private set; }

    public TicketStatus Status { get; private set; }

    public string EventTitle { get; private set; } = string.Empty;

    public string TierName { get; private set; } = string.Empty;

    public string? DateLabel { get; private set; }

    public DateTimeOffset DateStartsAt { get; private set; }

    /// <summary>
    ///     Midnight in Nairobi on the ticket's date: the QR (and the code behind it) is shown from then, and transfers
    ///     close then. Before the day nobody holds a scannable ticket, so there is nothing to resell or screenshot.
    /// </summary>
    public DateTimeOffset GoesLiveAt => NairobiTime.StartOfDayUtc(DateStartsAt.ToNairobiDate());

    public DateTimeOffset? DoorsOpenAt { get; private set; }

    public string? VenueName { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public int TransferCount { get; private set; }

    public DateTimeOffset? CheckedInAt { get; private set; }

    public Guid? CheckedInBy { get; private set; }

    public CheckInMethod? CheckInMethod { get; private set; }

    public static Ticket Issue(TicketId id, TicketIssue issue, TicketStatus status, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(issue);

        if (status is not (TicketStatus.Valid or TicketStatus.AwaitingBalance))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "New tickets are Valid or AwaitingBalance.");
        }

        var ticket = new Ticket(id)
        {
            OrderId = issue.OrderId,
            OrderLineId = issue.OrderLineId,
            EventId = issue.EventId,
            EventDateId = issue.EventDateId,
            TierId = issue.TierId,
            BundleId = issue.BundleId,
            Code = ShortCode.New(CodeLength),
            HolderPhone = issue.HolderPhone,
            HolderName = Fit(issue.HolderName),
            Status = status,
            EventTitle = Fit(issue.EventTitle),
            TierName = Fit(issue.TierName),
            DateLabel = issue.DateLabel is null ? null : Fit(issue.DateLabel),
            DateStartsAt = issue.DateStartsAt,
            DoorsOpenAt = issue.DoorsOpenAt,
            VenueName = issue.VenueName is null ? null : Fit(issue.VenueName),
            IssuedAt = now
        };

        ticket.RaiseEvent(new TicketIssued(id, issue.OrderId, issue.EventDateId, status));

        return ticket;
    }

    /// <summary>
    ///     Admits the holder (Spec 08 §8.2). The database decides races (a conditional update); this method is the rule
    ///     the update encodes and the reason returned when it does not apply.
    /// </summary>
    public CheckInOutcome CheckIn(Guid eventDateId, Guid staffUserId, CheckInMethod method, DateTimeOffset now)
    {
        var outcome = CheckInOutcomeFor(eventDateId);
        if (outcome != CheckInOutcome.Admitted)
        {
            return outcome;
        }

        var @event = new TicketCheckedIn(Id, EventDateId, staffUserId, method);

        Status = TicketStatus.CheckedIn;
        CheckedInAt = now;
        CheckedInBy = staffUserId;
        CheckInMethod = method;

        RaiseEvent(@event);

        return CheckInOutcome.Admitted;
    }

    /// <summary>What a scan of this ticket at <paramref name="eventDateId" /> would say, without changing it.</summary>
    public CheckInOutcome CheckInOutcomeFor(Guid eventDateId) =>
        Status switch
        {
            TicketStatus.CheckedIn => CheckInOutcome.AlreadyCheckedIn,
            TicketStatus.AwaitingBalance => CheckInOutcome.NotFullyPaid,
            TicketStatus.Void => CheckInOutcome.Invalid,
            _ when EventDateId != eventDateId => CheckInOutcome.WrongDate,
            _ => CheckInOutcome.Admitted
        };

    /// <summary>The Lipa Mdogo Mdogo balance is paid: the ticket becomes usable (and gets a QR).</summary>
    public void Activate()
    {
        if (Status != TicketStatus.AwaitingBalance)
        {
            return;
        }

        Status = TicketStatus.Valid;
        RaiseEvent(new TicketActivated(Id));
    }

    public void Void(string reason)
    {
        if (Status is TicketStatus.Void or TicketStatus.CheckedIn)
        {
            return;
        }

        Status = TicketStatus.Void;
        RaiseEvent(new TicketVoided(Id, reason));
    }

    public void AssignAttendee(string? name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? null : Fit(name.Trim());
        if (string.Equals(trimmed, AttendeeName, StringComparison.Ordinal))
        {
            return;
        }

        AttendeeName = trimmed;
        RaiseEvent(new TicketAttendeeAssigned(Id, trimmed is not null));
    }

    /// <summary>
    ///     Transfers are final (PRODUCT.md): the new holder gets the ticket immediately and the sender loses it. A new
    ///     code is issued so a screenshot of the old one stops working.
    /// </summary>
    public TransferOutcome Transfer(string toPhone, string toName, int maxTransfers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toPhone);

        if (Status != TicketStatus.Valid)
        {
            return TransferOutcome.NotTransferable;
        }

        if (TransferCount >= maxTransfers)
        {
            return TransferOutcome.LimitReached;
        }

        if (string.Equals(toPhone, HolderPhone, StringComparison.Ordinal))
        {
            return TransferOutcome.SameHolder;
        }

        var @event = new TicketTransferred(Id, TransferCount + 1);

        HolderPhone = toPhone;
        HolderName = Fit(string.IsNullOrWhiteSpace(toName) ? "Guest" : toName.Trim());
        AttendeeName = null;
        Code = ShortCode.New(CodeLength);
        TransferCount++;

        RaiseEvent(@event);

        return TransferOutcome.Transferred;
    }

    private static string Fit(string value) => value.Length <= NameMaxLength ? value : value[..NameMaxLength];
}

/// <summary>Snapshot of what a ticket shows, frozen when it is issued.</summary>
public sealed record TicketIssue(
    OrderId OrderId,
    OrderLineId OrderLineId,
    Guid EventId,
    Guid EventDateId,
    Guid TierId,
    Guid? BundleId,
    string HolderPhone,
    string HolderName,
    string EventTitle,
    string TierName,
    string? DateLabel,
    DateTimeOffset DateStartsAt,
    DateTimeOffset? DoorsOpenAt,
    string? VenueName);

public sealed record TicketIssued(TicketId TicketId, OrderId OrderId, Guid EventDateId, TicketStatus Status) : DomainEvent;

/// <summary>The closed set of check-in results (PRODUCT.md): each has its own result screen.</summary>
public enum CheckInOutcome
{
    Admitted = 1,
    AlreadyCheckedIn = 2,
    Invalid = 3,
    WrongDate = 4,
    NotFullyPaid = 5
}

public enum TransferOutcome
{
    Transferred = 1,
    NotTransferable = 2,
    LimitReached = 3,
    SameHolder = 4
}

public sealed record TicketCheckedIn(TicketId TicketId, Guid EventDateId, Guid StaffUserId, CheckInMethod Method) : DomainEvent;

public sealed record TicketActivated(TicketId TicketId) : DomainEvent;

public sealed record TicketVoided(TicketId TicketId, string Reason) : DomainEvent;

/// <summary>No phone numbers: the audit row records that a transfer happened, not to whom.</summary>
public sealed record TicketTransferred(TicketId TicketId, int TransferNumber) : DomainEvent;

/// <summary>No names in the event: the audit records only whether an attendee is set.</summary>
public sealed record TicketAttendeeAssigned(TicketId TicketId, bool Assigned) : DomainEvent;
