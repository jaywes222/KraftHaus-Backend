namespace Payments.Domain.Common;

/// <summary>Permissions the Payments admin endpoints require; must match Identity's catalog (an API test keeps them in step).</summary>
public static class PaymentsPermission
{
    /// <summary>Finance view of payments.</summary>
    public const string View = "payments:view";

    /// <summary>Re-check a payment against PalPluss.</summary>
    public const string Manage = "payments:manage";

    /// <summary>See tills (a Kraft's own, or all of them for Finance).</summary>
    public const string TillView = "till:view";

    /// <summary>
    ///     Submit a till for a Kraft (Kraft-scoped routes: Kraft Owner, Tenant Admin, Finance) and test, approve, reject or
    ///     disable tills (tenant-scoped routes: Tenant Admin and Finance only).
    /// </summary>
    public const string TillApprove = "till:approve";

    public static IReadOnlyList<string> All => [View, Manage, TillView, TillApprove];
}
