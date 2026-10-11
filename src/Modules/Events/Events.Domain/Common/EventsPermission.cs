namespace Events.Domain.Common;

/// <summary>
///     Permissions the Events endpoints require (Spec 08 §6.2). The role → permission matrix lives in Identity
///     (<c>IdentityPermissionMatrix</c>), which must list these exact strings; an API test keeps the two in step.
/// </summary>
public static class EventsPermission
{
    /// <summary>Read events and venues in the admin surface, including drafts.</summary>
    public const string View = "events:view";

    /// <summary>Create and edit events, venues, dates, tiers, phases, offers and policies.</summary>
    public const string Manage = "events:manage";

    /// <summary>Publish, cancel and complete events.</summary>
    public const string Publish = "events:publish";

    public static IReadOnlyList<string> All => [View, Manage, Publish];
}
