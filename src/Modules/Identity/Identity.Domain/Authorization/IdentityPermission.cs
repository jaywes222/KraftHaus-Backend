namespace Identity.Domain.Authorization;

public static class IdentityPermission
{
    public const string UsersView =
        "identity:users:view";

    public const string UsersManage =
        "identity:users:manage";

    public const string RolesView =
        "identity:roles:view";

    public const string RolesGrant =
        "identity:roles:grant";

    public const string RolesRevoke =
        "identity:roles:revoke";

    public const string InvitationsCreate =
        "identity:invitations:create";

    public const string InvitationsRevoke =
        "identity:invitations:revoke";

    public const string TicketsView =
        "tickets:view";

    public const string TicketsManage =
        "tickets:manage";

    public const string TicketsCheckIn =
        "tickets:checkin";

    public const string PaymentsView =
        "payments:view";

    public const string PaymentsManage =
        "payments:manage";

    public const string PaymentsRefund =
        "payments:refund";

    public const string TillView =
        "till:view";

    public const string TillApprove =
        "till:approve";

    public const string KitchenView =
        "kitchen:view";

    public const string KitchenHandle =
        "kitchen:handle";

    // Commerce module (UF-017): a Kraft's profile, brands, Krafters and menu.
    public const string KraftManage =
        "kraft:manage";

    // Displays module (UF-019): screens, their content and the programme shown on them.
    public const string DisplaysView =
        "displays:view";

    public const string DisplaysManage =
        "displays:manage";

    // Music module: the song-request queue in the DJ console. Must match Music.Domain.Common.MusicPermission.
    public const string MusicRequests =
        "music:requests";

    // Communications module (UF-020): the message log; broadcasts, templates and opt-outs.
    // Must match Communications.Domain.Common.CommunicationsPermission.
    public const string CommunicationsView =
        "communications:view";

    public const string CommunicationsManage =
        "communications:manage";

    // Feedback module (UF-010): anonymous ratings and comments. Must match Feedback.Domain.Common.FeedbackPermission.
    public const string FeedbackView =
        "feedback:view";

    // Content module (UF-024): articles and the Web Studio. Must match Content.Domain.Common.ContentPermission.
    public const string ContentManage =
        "content:manage";

    // Analytics module (UF-023): event dashboards (money included). Must match Analytics.Domain.AnalyticsPermission.
    public const string AnalyticsView =
        "analytics:view";

    // Events module (UF-016). Must match Events.Domain.Common.EventsPermission (kept in step by an API test).
    public const string EventsView =
        "events:view";

    public const string EventsManage =
        "events:manage";

    public const string EventsPublish =
        "events:publish";

    public static IReadOnlyList<string> All =>
    [
        UsersView,
        UsersManage,
        RolesView,
        RolesGrant,
        RolesRevoke,
        InvitationsCreate,
        InvitationsRevoke,
        TicketsView,
        TicketsManage,
        TicketsCheckIn,
        PaymentsView,
        PaymentsManage,
        PaymentsRefund,
        TillView,
        TillApprove,
        KitchenView,
        KitchenHandle,
        KraftManage,
        DisplaysView,
        DisplaysManage,
        MusicRequests,
        CommunicationsView,
        CommunicationsManage,
        FeedbackView,
        ContentManage,
        AnalyticsView,
        EventsView,
        EventsManage,
        EventsPublish
    ];
}
