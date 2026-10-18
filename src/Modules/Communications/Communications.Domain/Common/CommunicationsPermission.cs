namespace Communications.Domain.Common;

/// <summary>Permissions the Communications endpoints require; must match Identity's catalog (an API test keeps them in step).</summary>
public static class CommunicationsPermission
{
    /// <summary>See the message log and delivery status (Tenant Admin, Support).</summary>
    public const string View = "communications:view";

    /// <summary>Write and send broadcasts, keep templates and the opt-out list (Tenant Admin).</summary>
    public const string Manage = "communications:manage";

    public static IReadOnlyList<string> All => [View, Manage];
}
