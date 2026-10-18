namespace Analytics.Domain;

/// <summary>Permissions the Analytics endpoints require; must match Identity's catalog (an API test keeps them in step).</summary>
public static class AnalyticsPermission
{
    /// <summary>Event dashboards, money included (Tenant Admin, Finance).</summary>
    public const string View = "analytics:view";

    public static IReadOnlyList<string> All => [View];
}
