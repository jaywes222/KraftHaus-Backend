namespace Common.Domain.Communications;

/// <summary>Who a broadcast can go to for one event (UF-020).</summary>
public enum BroadcastAudience
{
    /// <summary>Everyone on the event's waitlist who agreed to marketing messages.</summary>
    WaitlistMarketing = 1,

    /// <summary>Waitlist members who agreed to marketing and have not bought a ticket yet ("sales are open").</summary>
    WaitlistWithoutTicket = 2,

    /// <summary>Everyone holding a usable ticket for the event: event-day information, not marketing.</summary>
    TicketHolders = 3
}
