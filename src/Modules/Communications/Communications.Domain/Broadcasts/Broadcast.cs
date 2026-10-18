using Common.Domain.Communications;
using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Communications.Domain.Common;

namespace Communications.Domain.Broadcasts;

/// <summary><c>Draft → Scheduled → Sending → Completed</c>; a draft or scheduled broadcast can be <c>Cancelled</c>.</summary>
public enum BroadcastStatus
{
    Draft = 1,
    Scheduled = 2,
    Sending = 3,
    Completed = 4,
    Cancelled = 5
}

/// <summary>
///     One SMS to an event audience (UF-020): written as a draft, scheduled (now or later), then sent by the throttled
///     dispatcher so a large broadcast never delays an OTP. Marketing audiences skip opted-out numbers.
/// </summary>
public sealed class Broadcast : AggregateRoot<BroadcastId>
{
    public const int NameMaxLength = 80;

    /// <summary>Three SMS parts (GSM-7), the most one broadcast may cost per recipient.</summary>
    public const int BodyMaxLength = 459;

    public const string FirstNamePlaceholder = "{firstName}";

    /// <summary>What <see cref="FirstNamePlaceholder" /> becomes when the name is unknown.</summary>
    public const string FirstNameFallback = "there";

    private Broadcast(BroadcastId id)
        : base(id)
    {
    }

    /// <summary>For staff only ("Sales open: waitlist").</summary>
    public string Name { get; private set; } = string.Empty;

    public Guid EventId { get; private set; }

    public BroadcastAudience Audience { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public BroadcastStatus Status { get; private set; }

    public DateTimeOffset? ScheduledFor { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Messages queued when sending started (after opt-outs and duplicates were removed).</summary>
    public int Recipients { get; private set; }

    /// <summary>Numbers left out because they opted out of marketing.</summary>
    public int Skipped { get; private set; }

    public int Sent { get; private set; }

    public int Failed { get; private set; }

    /// <summary>Marketing audiences need consent and respect opt-outs; ticket-holder information does not.</summary>
    public bool IsMarketing => IsMarketingAudience(Audience);

    public static bool IsMarketingAudience(BroadcastAudience audience) => audience != BroadcastAudience.TicketHolders;

    public static Result<Broadcast> Create(
        BroadcastId id,
        string? name,
        Guid eventId,
        BroadcastAudience audience,
        string? body)
    {
        var valid = Validate(name, eventId, audience, body);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var broadcast = new Broadcast(id)
        {
            Name = valid.Value.Name,
            EventId = eventId,
            Audience = audience,
            Body = valid.Value.Body,
            Status = BroadcastStatus.Draft
        };

        broadcast.RaiseEvent(new BroadcastCreated(id, eventId, audience));
        return broadcast;
    }

    public Result Update(string? name, BroadcastAudience audience, string? body)
    {
        if (Status != BroadcastStatus.Draft)
        {
            return CommunicationsErrors.NotEditable;
        }

        var valid = Validate(name, EventId, audience, body);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        if (valid.Value.Name == Name && audience == Audience && valid.Value.Body == Body)
        {
            return Result.Success;
        }

        var @event = new BroadcastUpdated(Id, EventId, audience);

        Name = valid.Value.Name;
        Audience = audience;
        Body = valid.Value.Body;

        RaiseEvent(@event);
        return Result.Success;
    }

    /// <summary>Queues the broadcast for <paramref name="sendAt" /> (or right away). A scheduled one can be moved.</summary>
    public Result Schedule(DateTimeOffset? sendAt, DateTimeOffset now, int maxDaysAhead)
    {
        if (Status is not (BroadcastStatus.Draft or BroadcastStatus.Scheduled))
        {
            return CommunicationsErrors.AlreadyFinished;
        }

        var at = sendAt is { } requested && requested > now ? requested : now;
        if (at > now.AddDays(maxDaysAhead))
        {
            return CommunicationsErrors.Invalid("BroadcastInvalid", [$"SendAt: Schedule at most {maxDaysAhead} days ahead."]);
        }

        var @event = new BroadcastScheduled(Id, EventId, at);

        Status = BroadcastStatus.Scheduled;
        ScheduledFor = at;

        RaiseEvent(@event);
        return Result.Success;
    }

    public Result Cancel(DateTimeOffset now)
    {
        if (Status is BroadcastStatus.Completed or BroadcastStatus.Cancelled)
        {
            return CommunicationsErrors.AlreadyFinished;
        }

        if (Status == BroadcastStatus.Sending)
        {
            return CommunicationsErrors.NotCancellable;
        }

        var @event = new BroadcastCancelled(Id, EventId);

        Status = BroadcastStatus.Cancelled;
        CancelledAt = now;

        RaiseEvent(@event);
        return Result.Success;
    }

    public bool IsDue(DateTimeOffset now) => Status == BroadcastStatus.Scheduled && ScheduledFor <= now;

    /// <summary>The dispatcher has queued <paramref name="recipients" /> messages; sending starts.</summary>
    public Result Start(int recipients, int skipped, DateTimeOffset now)
    {
        if (!IsDue(now))
        {
            return CommunicationsErrors.Conflict("NotDue", "The broadcast is not due to send.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(recipients);
        ArgumentOutOfRangeException.ThrowIfNegative(skipped);

        var @event = new BroadcastStarted(Id, EventId, recipients, skipped);

        Status = BroadcastStatus.Sending;
        StartedAt = now;
        Recipients = recipients;
        Skipped = skipped;

        RaiseEvent(@event);
        return Result.Success;
    }

    /// <summary>Counts from one dispatcher batch. Not an audited event (a broadcast can take many batches).</summary>
    public void RecordBatch(int sent, int failed)
    {
        if (Status != BroadcastStatus.Sending)
        {
            return;
        }

        Sent += Math.Max(0, sent);
        Failed += Math.Max(0, failed);
    }

    public void Complete(DateTimeOffset now)
    {
        if (Status != BroadcastStatus.Sending)
        {
            return;
        }

        var @event = new BroadcastCompleted(Id, EventId, Sent, Failed);

        Status = BroadcastStatus.Completed;
        CompletedAt = now;

        RaiseEvent(@event);
    }

    /// <summary>The text one recipient gets: <c>{firstName}</c> filled in, then the marketing footer when it applies.</summary>
    public string RenderFor(string? firstName, string? marketingFooter)
    {
        var name = string.IsNullOrWhiteSpace(firstName) ? FirstNameFallback : firstName.Trim();
        var text = Body.Replace(FirstNamePlaceholder, name, StringComparison.OrdinalIgnoreCase);

        return IsMarketing && !string.IsNullOrWhiteSpace(marketingFooter)
            ? $"{text} {marketingFooter.Trim()}"
            : text;
    }

    private static Result<(string Name, string Body)> Validate(string? name, Guid eventId, BroadcastAudience audience, string? body)
    {
        var errors = new List<string>();
        var trimmedName = name?.Trim() ?? string.Empty;
        var trimmedBody = body?.Trim() ?? string.Empty;

        if (trimmedName.Length is 0 or > NameMaxLength)
        {
            errors.Add($"Name: Give it a name of up to {NameMaxLength} characters.");
        }

        if (eventId == Guid.Empty)
        {
            errors.Add("EventId: Choose the event.");
        }

        if (!Enum.IsDefined(audience))
        {
            errors.Add("Audience: Choose who it goes to.");
        }

        if (trimmedBody.Length == 0)
        {
            errors.Add("Body: Write the message.");
        }
        else if (trimmedBody.Length > BodyMaxLength)
        {
            errors.Add($"Body: Keep it to {BodyMaxLength} characters (three SMS).");
        }

        return errors.Count > 0
            ? CommunicationsErrors.Invalid("BroadcastInvalid", errors)
            : (trimmedName, trimmedBody);
    }
}
