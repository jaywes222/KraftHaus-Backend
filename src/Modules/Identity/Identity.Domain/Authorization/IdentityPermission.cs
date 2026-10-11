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
        EventsView,
        EventsManage,
        EventsPublish
    ];
}
