using System.Net.Mail;
using Common.Domain.Tenancy;
using Identity.Domain.Authorization;
using Identity.Domain.Common;
using Identity.Domain.Invitations;
using Identity.Domain.Otp;
using Identity.Domain.Roles;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Xunit;

namespace Identity.Tests;

public sealed class IdentityDomainTests
{
    private static readonly DateTimeOffset _now =
        new(2026, 10, 8, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PhoneNumberNormalizesKenyanMobileNumber()
    {
        var result = PhoneNumber.Create("0712 345 678");

        Assert.False(result.IsFailure);
        Assert.Equal("+254712345678", result.Value!.Value);
    }

    [Fact]
    public void PhoneNumberRejectsUnsupportedNumber()
    {
        var result = PhoneNumber.Create("12345");

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.PhoneNumberInvalid", result.Error!.Key);
    }

    [Fact]
    public void EmailAddressNormalizesToUpperCase()
    {
        var result = EmailAddress.Create(" user@example.com ");

        Assert.False(result.IsFailure);
        Assert.Equal("USER@EXAMPLE.COM", result.Value!.Value);
    }

    [Fact]
    public void PolicyRefRejectsInvalidVersion()
    {
        var result = PolicyRef.Create(
            "terms",
            0,
            "hash");

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.VersionInvalid", result.Error!.Key);
    }

    [Fact]
    public void ApplicationUserRegistersVerifiedPhoneWithTwoConsents()
    {
        var userId = new Common.Domain.StronglyTypedIds.ApplicationUserId(
            Guid.CreateVersion7());
        var phone = PhoneNumber.Create("0712345678").Value!;
        var terms = PolicyRef.Create("terms", 1, "terms-hash").Value!;
        var privacy = PolicyRef.Create("privacy", 1, "privacy-hash").Value!;

        var result = ApplicationUser.RegisterVerifiedPhone(
            userId,
            IdentityScope.Platform,
            null,
            phone,
            " Michael ",
            terms,
            privacy,
            _now);

        Assert.False(result.IsFailure);

        var user = result.Value!;

        Assert.Equal(userId, user.Id);
        Assert.Equal(IdentityScope.Platform, user.Scope);
        Assert.Null(user.TenantId);
        Assert.Equal("+254712345678", user.Phone!.Value);
        Assert.Equal("Michael", user.DisplayName);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(2, user.Consents.Count);
        Assert.Contains(user.Consents, x => x.Kind == ConsentKind.Terms && x.Granted);
        Assert.Contains(user.Consents, x => x.Kind == ConsentKind.Privacy && x.Granted);
    }

    [Fact]
    public void ApplicationUserRejectsTenantUserWithoutTenantId()
    {
        var result = ApplicationUser.RegisterVerifiedEmail(
            new Common.Domain.StronglyTypedIds.ApplicationUserId(Guid.CreateVersion7()),
            IdentityScope.Tenant,
            null,
            EmailAddress.Create("user@example.com").Value!,
            "User",
            PolicyRef.Create("terms", 1, "hash").Value!,
            PolicyRef.Create("privacy", 1, "hash").Value!,
            _now);

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.TenantIdRequired", result.Error!.Key);
    }

    [Fact]
    public void ApplicationUserRejectsPlatformUserWithTenantId()
    {
        var tenantId = new TenantId(Guid.CreateVersion7());

        var result = ApplicationUser.RegisterVerifiedEmail(
            new Common.Domain.StronglyTypedIds.ApplicationUserId(Guid.CreateVersion7()),
            IdentityScope.Platform,
            tenantId,
            EmailAddress.Create("user@example.com").Value!,
            "User",
            PolicyRef.Create("terms", 1, "hash").Value!,
            PolicyRef.Create("privacy", 1, "hash").Value!,
            _now);

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.TenantIdInvalid", result.Error!.Key);
    }

    [Fact]
    public void ApplicationUserDisabledUserCannotSignIn()
    {
        var user = CreateUser();

        Assert.False(user.Disable().IsFailure);

        var result = user.RecordSignIn(_now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.UserDisabled", result.Error!.Key);
    }

    [Fact]
    public void RoleScopeRulesEnforceRoleBoundaries()
    {
        var tenantId = Guid.CreateVersion7();
        var kraftId = Guid.CreateVersion7();

        Assert.False(RoleScopeRules.Validate(
            IdentityRole.SuperAdmin,
            RoleScopeType.Platform,
            null).IsFailure);

        Assert.False(RoleScopeRules.Validate(
            IdentityRole.TenantAdmin,
            RoleScopeType.Tenant,
            tenantId).IsFailure);

        Assert.False(RoleScopeRules.Validate(
            IdentityRole.KraftStaff,
            RoleScopeType.Kraft,
            kraftId).IsFailure);

        Assert.True(RoleScopeRules.Validate(
            IdentityRole.SuperAdmin,
            RoleScopeType.Tenant,
            tenantId).IsFailure);
    }

    [Fact]
    public void RoleAssignmentGrantAndRevokeAreStateful()
    {
        var userId = new Common.Domain.StronglyTypedIds.ApplicationUserId(Guid.CreateVersion7());
        var assignmentResult = RoleAssignment.Grant(
            new RoleAssignmentId(Guid.CreateVersion7()),
            userId,
            IdentityRole.TenantAdmin,
            RoleScopeType.Tenant,
            Guid.CreateVersion7(),
            _now);

        Assert.False(assignmentResult.IsFailure);

        var assignment = assignmentResult.Value!;
        Assert.Equal(RoleAssignmentStatus.Active, assignment.Status);

        var revoke = assignment.Revoke(_now.AddMinutes(1));

        Assert.False(revoke.IsFailure);
        Assert.Equal(RoleAssignmentStatus.Revoked, assignment.Status);
        Assert.NotNull(assignment.RevokedAt);

        var secondRevoke = assignment.Revoke(_now.AddMinutes(2));
        Assert.True(secondRevoke.IsFailure);
        Assert.Equal("Identity.RoleAlreadyRevoked", secondRevoke.Error!.Key);
    }

    [Fact]
    public void StaffInvitationRequiresCorrectIdentityAndCanBeAccepted()
    {
        var user = CreateUser();
        var invitationResult = StaffInvitation.ForPhone(
            new StaffInvitationId(Guid.CreateVersion7()),
            new TenantId(Guid.CreateVersion7()),
            new Common.Domain.StronglyTypedIds.ApplicationUserId(Guid.CreateVersion7()),
            user.Phone!.Value,
            IdentityRole.TenantAdmin,
            RoleScopeType.Tenant,
            Guid.CreateVersion7(),
            _now);

        Assert.False(invitationResult.IsFailure);

        var invitation = invitationResult.Value!;
        var accept = invitation.Accept(user, _now.AddMinutes(1));

        Assert.False(accept.IsFailure);
        Assert.Equal(StaffInvitationStatus.Accepted, invitation.Status);
        Assert.NotNull(invitation.AcceptedAt);
    }

    [Fact]
    public void StaffInvitationRejectsDestinationMismatch()
    {
        var user = CreateUser();
        var invitationResult = StaffInvitation.ForPhone(
            new StaffInvitationId(Guid.CreateVersion7()),
            new TenantId(Guid.CreateVersion7()),
            new Common.Domain.StronglyTypedIds.ApplicationUserId(Guid.CreateVersion7()),
            "+254711111111",
            IdentityRole.TenantAdmin,
            RoleScopeType.Tenant,
            Guid.CreateVersion7(),
            _now);

        Assert.False(invitationResult.IsFailure);

        var result = invitationResult.Value!.Accept(user, _now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Identity.InvitationDestinationMismatch",
            result.Error!.Key);
    }

    [Fact]
    public void StaffInvitationExpiresAfterSevenDays()
    {
        var invitation = StaffInvitation.ForEmail(
            new StaffInvitationId(Guid.CreateVersion7()),
            new TenantId(Guid.CreateVersion7()),
            new Common.Domain.StronglyTypedIds.ApplicationUserId(Guid.CreateVersion7()),
            "USER@EXAMPLE.COM",
            IdentityRole.TenantAdmin,
            RoleScopeType.Tenant,
            Guid.CreateVersion7(),
            _now).Value!;

        Assert.False(invitation.IsExpired(_now.AddDays(7).AddTicks(-1)));
        Assert.True(invitation.IsExpired(_now.AddDays(7)));
    }

    [Fact]
    public void PermissionMatrixContainsEveryRolePermissionCell()
    {
        var roles =
            Enum.GetValues<IdentityRole>();

        var expectedCellCount =
            roles.Length *
            IdentityPermission.All.Count;

        var cells =
            IdentityPermissionMatrix.GetCells();

        Assert.Equal(
            expectedCellCount,
            cells.Count);

        Assert.Equal(
            expectedCellCount,
            cells
                .Select(
                    x =>
                        $"{x.Role}:{x.Permission}")
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Theory]
    [MemberData(nameof(PermissionMatrixCells))]
    public void PermissionMatrixCellMatchesDeclaredRolePermission(
    IdentityRole role,
    string permission,
    bool expected)
    {
        Assert.Equal(
            expected,
            IdentityPermissionMatrix.HasPermission(
                role,
                permission));
    }

    public static IEnumerable<object[]> PermissionMatrixCells()
    {
        foreach (var (Role, Permission, Allowed) in
            IdentityPermissionMatrix.GetCells())
        {
            yield return
            [
                Role,
                Permission,
                Allowed
            ];
        }
    }

    [Fact]
    public void PermissionMatrixAllowsSuperAdminOnPlatform()
    {
        Assert.True(
            IdentityPermissionMatrix.HasPermission(
                IdentityRole.SuperAdmin,
                IdentityPermission.UsersManage));
    }

    [Fact]
    public void PermissionMatrixAllowsFinanceToApproveTill()
    {
        Assert.True(
            IdentityPermissionMatrix.HasPermission(
                IdentityRole.Finance,
                IdentityPermission.TillApprove));
    }

    [Fact]
    public void PermissionMatrixDoesNotGiveFinanceKitchenAccess()
    {
        Assert.False(
            IdentityPermissionMatrix.HasPermission(
                IdentityRole.Finance,
                IdentityPermission.KitchenHandle));
    }

    [Fact]
    public void PermissionMatrixDoesNotGiveKraftStaffRoleManagement()
    {
        Assert.False(
            IdentityPermissionMatrix.HasPermission(
                IdentityRole.KraftStaff,
                IdentityPermission.RolesGrant));
    }

    private static ApplicationUser CreateUser()
    {
        return ApplicationUser.RegisterVerifiedPhone(
            new Common.Domain.StronglyTypedIds.ApplicationUserId(Guid.CreateVersion7()),
            IdentityScope.Platform,
            null,
            PhoneNumber.Create("0712345678").Value!,
            "Michael",
            PolicyRef.Create("terms", 1, "terms-hash").Value!,
            PolicyRef.Create("privacy", 1, "privacy-hash").Value!,
            _now).Value!;
    }

    [Fact]
    public void RevokedSessionCannotRotateRefreshToken()
    {
        var sessionResult =
            AuthSession.Start(
                new AuthSessionId(
                    Guid.CreateVersion7()),
                new Common.Domain.StronglyTypedIds.ApplicationUserId(
                    Guid.CreateVersion7()),
                "device-1",
                "hash-1",
                _now);

        Assert.False(
            sessionResult.IsFailure);

        var session =
            sessionResult.Value!;

        var revokeResult =
            session.Revoke(
                SessionRevocationReason.SignedOut,
                _now);

        Assert.False(
            revokeResult.IsFailure);

        var rotateResult =
            session.Rotate(
                1,
                "hash-1",
                "hash-2",
                _now);

        Assert.True(
            rotateResult.IsFailure);

        Assert.Equal(
            "Identity.SessionNotActive",
            rotateResult.Error!.Key);

        Assert.Equal(
            AuthSessionStatus.Revoked,
            session.Status);
    }
}
