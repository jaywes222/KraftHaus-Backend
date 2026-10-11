using Identity.Domain.Roles;

namespace Identity.Domain.Authorization;

public static class IdentityPermissionMatrix
{
    private static readonly Dictionary<
        IdentityRole,
        IReadOnlySet<string>> _matrix =
        new()
        {
            [IdentityRole.SuperAdmin] =
                All(),

            [IdentityRole.TenantAdmin] =
                Permissions(
                    IdentityPermission.UsersView,
                    IdentityPermission.UsersManage,
                    IdentityPermission.RolesView,
                    IdentityPermission.RolesGrant,
                    IdentityPermission.RolesRevoke,
                    IdentityPermission.InvitationsCreate,
                    IdentityPermission.InvitationsRevoke,
                    IdentityPermission.TicketsView,
                    IdentityPermission.TicketsManage,
                    IdentityPermission.TicketsCheckIn,
                    IdentityPermission.PaymentsView,
                    IdentityPermission.PaymentsManage,
                    IdentityPermission.PaymentsRefund,
                    IdentityPermission.TillView,
                    IdentityPermission.TillApprove,
                    IdentityPermission.KitchenView,
                    IdentityPermission.KitchenHandle,
                    IdentityPermission.EventsView,
                    IdentityPermission.EventsManage,
                    IdentityPermission.EventsPublish),

            [IdentityRole.Finance] =
                Permissions(
                    IdentityPermission.EventsView,
                    IdentityPermission.PaymentsView,
                    IdentityPermission.PaymentsManage,
                    IdentityPermission.PaymentsRefund,
                    IdentityPermission.TillView,
                    IdentityPermission.TillApprove),

            [IdentityRole.Support] =
                Permissions(
                    IdentityPermission.EventsView,
                    IdentityPermission.UsersView,
                    IdentityPermission.TicketsView,
                    IdentityPermission.TicketsManage,
                    IdentityPermission.TicketsCheckIn,
                    IdentityPermission.PaymentsView),

            [IdentityRole.KraftOwner] =
                Permissions(
                    IdentityPermission.UsersView,
                    IdentityPermission.TicketsView,
                    IdentityPermission.TicketsManage,
                    IdentityPermission.TicketsCheckIn,
                    IdentityPermission.PaymentsView,
                    IdentityPermission.PaymentsManage,
                    IdentityPermission.PaymentsRefund,
                    IdentityPermission.TillView,
                    IdentityPermission.TillApprove,
                    IdentityPermission.KitchenView,
                    IdentityPermission.KitchenHandle),

            [IdentityRole.KraftStaff] =
                Permissions(
                    IdentityPermission.TicketsView,
                    IdentityPermission.TicketsCheckIn,
                    IdentityPermission.PaymentsView,
                    IdentityPermission.TillView,
                    IdentityPermission.KitchenView,
                    IdentityPermission.KitchenHandle)
        };

    private static readonly IReadOnlySet<string> _emptyPermissions =
        new HashSet<string>(StringComparer.Ordinal);

    public static IReadOnlySet<string> GetPermissions(
        IdentityRole role)
    {
        return _matrix.TryGetValue(
            role,
            out var permissions)
                ? permissions
                : _emptyPermissions;
    }

    public static bool HasPermission(
        IdentityRole role,
        string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return GetPermissions(role)
            .Contains(permission);
    }

    public static IReadOnlyList<
        (IdentityRole Role, string Permission, bool Allowed)>
        GetCells()
    {
        return
        [
            .. Enum.GetValues<IdentityRole>()
                .SelectMany(
                    role =>
                        IdentityPermission.All.Select(
                            permission =>
                                (
                                    Role: role,
                                    Permission: permission,
                                    Allowed:
                                        HasPermission(
                                            role,
                                            permission))))
        ];
    }

    private static HashSet<string> All()
    {
        return new HashSet<string>(
            IdentityPermission.All,
            StringComparer.Ordinal);
    }

    private static HashSet<string> Permissions(
        params string[] permissions)
    {
        return new HashSet<string>(
            permissions,
            StringComparer.Ordinal);
    }
}
