namespace Music.Domain.Common;

/// <summary>Permissions the Music endpoints require; must match Identity's catalog (an API test keeps them in step).</summary>
public static class MusicPermission
{
    /// <summary>Run the DJ console: open and close requests for an event, and work the queue (Tenant Admin, Support).</summary>
    public const string ManageRequests = "music:requests";

    public static IReadOnlyList<string> All => [ManageRequests];
}
