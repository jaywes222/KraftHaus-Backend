namespace Ticketing.Domain.Common;

/// <summary>Permissions the Ticketing endpoints require; must match Identity's catalog (an API test keeps them in step).</summary>
public static class TicketingPermission
{
    /// <summary>See orders and sales counters.</summary>
    public const string View = "tickets:view";

    /// <summary>Waitlist export and order corrections.</summary>
    public const string Manage = "tickets:manage";

    /// <summary>Scan and admit at the door (UF-012, UF-013).</summary>
    public const string CheckIn = "tickets:checkin";

    /// <summary>
    ///     Export the waitlist (names, phones, ages, gender). Personal data, so it rides on the event-setup permission
    ///     that only Tenant Admins hold, not on <see cref="Manage" /> (which Support also has).
    /// </summary>
    public const string ExportWaitlist = "events:manage";

    public static IReadOnlyList<string> All => [View, Manage, CheckIn, ExportWaitlist];
}
