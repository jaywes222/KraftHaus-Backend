using Common.Domain.Aggregates;
using Common.Domain.Events;
using Communications.Domain.Common;

namespace Communications.Domain.OptOuts;

public enum OptOutSource
{
    /// <summary>The customer switched marketing messages off themselves.</summary>
    Customer = 1,

    /// <summary>Staff added the number (asked by phone, a STOP reply forwarded by the provider, ...).</summary>
    Staff = 2
}

/// <summary>
///     A number that receives no marketing broadcasts (Kenya Data Protection Act: marketing needs consent and an easy
///     way out). One row per phone; switching back on keeps the row, inactive, so the history stays.
/// </summary>
public sealed class OptOut : AggregateRoot<OptOutId>
{
    private OptOut(OptOutId id)
        : base(id)
    {
    }

    /// <summary>E.164.</summary>
    public string Phone { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public OptOutSource Source { get; private set; }

    public DateTimeOffset OptedOutAt { get; private set; }

    public DateTimeOffset? OptedInAt { get; private set; }

    public static OptOut Create(OptOutId id, string phone, OptOutSource source, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phone);

        var optOut = new OptOut(id)
        {
            Phone = phone,
            IsActive = true,
            Source = source,
            OptedOutAt = now
        };

        optOut.RaiseEvent(new PhoneOptedOut(id, source));
        return optOut;
    }

    /// <summary>Marketing off again (idempotent).</summary>
    public void Activate(OptOutSource source, DateTimeOffset now)
    {
        if (IsActive)
        {
            return;
        }

        var @event = new PhoneOptedOut(Id, source);

        IsActive = true;
        Source = source;
        OptedOutAt = now;
        OptedInAt = null;

        RaiseEvent(@event);
    }

    /// <summary>Marketing back on (idempotent).</summary>
    public void Deactivate(OptOutSource source, DateTimeOffset now)
    {
        if (!IsActive)
        {
            return;
        }

        var @event = new PhoneOptedIn(Id, source);

        IsActive = false;
        Source = source;
        OptedInAt = now;

        RaiseEvent(@event);
    }
}

/// <summary>No phone in the event: audit metadata never carries personal data.</summary>
public sealed record PhoneOptedOut(OptOutId OptOutId, OptOutSource Source) : DomainEvent;

public sealed record PhoneOptedIn(OptOutId OptOutId, OptOutSource Source) : DomainEvent;
