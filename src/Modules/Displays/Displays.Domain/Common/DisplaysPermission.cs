namespace Displays.Domain.Common;

/// <summary>Permissions the Displays endpoints require; must match Identity's catalog (an API test keeps them in step).</summary>
public static class DisplaysPermission
{
    /// <summary>See screens, their status, configurations and the programme (Tenant Admin, Support).</summary>
    public const string View = "displays:view";

    /// <summary>Pair, configure, schedule and revoke screens; edit content and the programme (Tenant Admin).</summary>
    public const string Manage = "displays:manage";

    public static IReadOnlyList<string> All => [View, Manage];
}
