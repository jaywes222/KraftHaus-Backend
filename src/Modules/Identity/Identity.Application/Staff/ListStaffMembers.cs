using Common.Application.Commerce;
using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Identity.Domain.Users;

namespace Identity.Application.Staff;

/// <summary>One person holding at least one staff role on the host's tenant.</summary>
public sealed record StaffMemberResult(
    ApplicationUserId UserId,
    string DisplayName,
    string? Phone,
    string? Email,
    UserStatus Status,
    DateTimeOffset? LastSignedInAt,
    IReadOnlyList<StaffMemberRoleResult> Roles);

/// <param name="ScopeName">The Kraft's name for a Kraft role; null for a tenant role.</param>
public sealed record StaffMemberRoleResult(
    RoleAssignmentId AssignmentId,
    IdentityRole Role,
    RoleScopeType ScopeType,
    Guid? ScopeId,
    string? ScopeName,
    DateTimeOffset GrantedAt);

/// <summary>
///     The staff list for the host's tenant (UF-022): users of the host's identity realm with an active role that
///     applies here. Tenant roles must name this tenant. Kraft roles are kept only when the Kraft exists in this
///     tenant's own database (<see cref="IKraftDirectory" />), because a platform-scoped realm is shared by every
///     platform-scoped tenant and its users may hold Kraft roles elsewhere. With no Kraft directory registered
///     (Commerce disabled) Kraft roles are left out rather than shown unverified. Platform roles (Super Admin) are
///     not tenant staff and are never listed.
/// </summary>
public sealed class ListStaffMembersService(
    IStaffDirectory directory,
    IEnumerable<IKraftDirectory> kraftDirectories,
    ICurrentTenant currentTenant)
{
    public async Task<Result<IReadOnlyList<StaffMemberResult>>> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!currentTenant.IsResolved)
        {
            return IdentityErrors.InvalidState("TenantUnresolved", "Listing staff requires a tenant host.");
        }

        var tenantId = currentTenant.TenantId.Value;
        var realm = IdentityRealm.ForHost(currentTenant.TenantId, currentTenant.IdentityScope);
        var rows = await directory.ListActiveAssignmentsAsync(realm, cancellationToken);

        var kraftNames = await KraftNamesAsync(rows, cancellationToken);

        var applicable = rows
            .Where(x =>
                (x.ScopeType == RoleScopeType.Tenant && x.ScopeId == tenantId) ||
                (x.ScopeType == RoleScopeType.Kraft && x.ScopeId is { } kraftId && kraftNames.ContainsKey(kraftId)))
            .ToList();

        IReadOnlyList<StaffMemberResult> members =
        [
            .. applicable
                .GroupBy(x => x.UserId)
                .Select(group =>
                {
                    var user = group.First();

                    return new StaffMemberResult(
                        user.UserId,
                        user.DisplayName,
                        user.Phone,
                        user.Email,
                        user.UserStatus,
                        user.LastSignedInAt,
                        [
                            .. group
                                .OrderBy(x => x.GrantedAt)
                                .Select(x => new StaffMemberRoleResult(
                                    x.AssignmentId,
                                    x.Role,
                                    x.ScopeType,
                                    x.ScopeId,
                                    x.ScopeType == RoleScopeType.Kraft && x.ScopeId is { } id ? kraftNames[id] : null,
                                    x.GrantedAt))
                        ]);
                })
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.UserId.Value)
        ];

        return Result<IReadOnlyList<StaffMemberResult>>.Success(members);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> KraftNamesAsync(
        IReadOnlyList<StaffAssignmentRow> rows,
        CancellationToken cancellationToken)
    {
        var kraftIds = rows
            .Where(x => x.ScopeType == RoleScopeType.Kraft && x.ScopeId is not null)
            .Select(x => x.ScopeId!.Value)
            .Distinct()
            .ToList();

        var kraftDirectory = kraftDirectories.FirstOrDefault();

        return kraftIds.Count == 0 || kraftDirectory is null
            ? new Dictionary<Guid, string>()
            : await kraftDirectory.NamesAsync(kraftIds, cancellationToken);
    }
}
