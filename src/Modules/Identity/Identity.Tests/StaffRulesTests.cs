using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Identity.Application.Authorization;
using Identity.Application.Staff;
using Identity.Domain.Authorization;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Identity.Infrastructure.Otp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace Identity.Tests;

public sealed class StaffRulesTests
{
    private static readonly TenantId _tenant = new(Guid.CreateVersion7());

    [Theory]
    [InlineData(IdentityRole.TenantAdmin)]
    [InlineData(IdentityRole.Finance)]
    [InlineData(IdentityRole.Support)]
    public void TenantRolesApplyToTheHostTenant(IdentityRole role)
    {
        var scope = StaffRoleScope.Resolve(role, _tenant, kraftId: null);

        Assert.False(scope.IsFailure);
        Assert.Equal((RoleScopeType.Tenant, (Guid?)_tenant.Value), scope.Value);
    }

    [Fact]
    public void TenantRoleWithKraftIdIsRejected() =>
        Assert.Equal("Identity.KraftIdInvalid", StaffRoleScope.Resolve(IdentityRole.Finance, _tenant, Guid.NewGuid()).Error!.Key);

    [Theory]
    [InlineData(IdentityRole.KraftOwner)]
    [InlineData(IdentityRole.KraftStaff)]
    public void KraftRolesNeedAKraftId(IdentityRole role)
    {
        var kraftId = Guid.NewGuid();

        Assert.Equal("Identity.KraftIdRequired", StaffRoleScope.Resolve(role, _tenant, null).Error!.Key);
        Assert.Equal("Identity.KraftIdRequired", StaffRoleScope.Resolve(role, _tenant, Guid.Empty).Error!.Key);
        Assert.Equal((RoleScopeType.Kraft, (Guid?)kraftId), StaffRoleScope.Resolve(role, _tenant, kraftId).Value);
    }

    [Fact]
    public void SuperAdminCannotBeGrantedFromATenant()
    {
        var error = StaffRoleScope.Resolve(IdentityRole.SuperAdmin, _tenant, null).Error!;

        Assert.Equal("Identity.RoleNotGrantable", error.Key);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, error.StatusCode);
    }

    [Fact]
    public void AssignmentForAnotherTenantDoesNotBelongToThisOne()
    {
        var other = Grant(IdentityRole.Support, RoleScopeType.Tenant, Guid.CreateVersion7());
        var mine = Grant(IdentityRole.Support, RoleScopeType.Tenant, _tenant.Value);

        Assert.False(StaffRoleScope.BelongsToTenant(other, _tenant));
        Assert.True(StaffRoleScope.BelongsToTenant(mine, _tenant));
    }

    [Fact]
    public async Task SuperAdminHasEveryPermissionAtEveryScope()
    {
        var superAdmin = Grant(IdentityRole.SuperAdmin, RoleScopeType.Platform, null);
        var service = new PermissionAuthorizationService(new FixedAssignments(superAdmin));

        Assert.True(await service.HasPermissionAsync(superAdmin.UserId, IdentityPermission.RolesGrant, PermissionScope.Tenant(_tenant.Value), CancellationToken.None));
        Assert.True(await service.HasPermissionAsync(superAdmin.UserId, IdentityPermission.KitchenHandle, PermissionScope.Kraft(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task TenantAdminHasNoPermissionInAnotherTenant()
    {
        var admin = Grant(IdentityRole.TenantAdmin, RoleScopeType.Tenant, _tenant.Value);
        var service = new PermissionAuthorizationService(new FixedAssignments(admin));

        Assert.True(await service.HasPermissionAsync(admin.UserId, IdentityPermission.RolesGrant, PermissionScope.Tenant(_tenant.Value), CancellationToken.None));
        Assert.False(await service.HasPermissionAsync(admin.UserId, IdentityPermission.RolesGrant, PermissionScope.Tenant(Guid.CreateVersion7()), CancellationToken.None));
    }

    [Fact]
    public async Task TenantRolesCoverTheKraftsOfTheirOwnTenantOnly()
    {
        var admin = Grant(IdentityRole.TenantAdmin, RoleScopeType.Tenant, _tenant.Value);
        var service = new PermissionAuthorizationService(new FixedAssignments(admin));
        var kraft = Guid.CreateVersion7();

        Assert.True(await service.HasPermissionAsync(admin.UserId, IdentityPermission.KitchenHandle, PermissionScope.Kraft(kraft, _tenant.Value), CancellationToken.None));
        Assert.False(await service.HasPermissionAsync(admin.UserId, IdentityPermission.KitchenHandle, PermissionScope.Kraft(kraft, Guid.CreateVersion7()), CancellationToken.None));
        Assert.False(await service.HasPermissionAsync(admin.UserId, IdentityPermission.KitchenHandle, PermissionScope.Kraft(kraft), CancellationToken.None));
    }

    [Fact]
    public async Task KraftRolesNeverCoverAnotherKraftOrTheTenant()
    {
        var kraft = Guid.CreateVersion7();
        var staff = Grant(IdentityRole.KraftStaff, RoleScopeType.Kraft, kraft);
        var service = new PermissionAuthorizationService(new FixedAssignments(staff));

        Assert.True(await service.HasPermissionAsync(staff.UserId, IdentityPermission.KitchenHandle, PermissionScope.Kraft(kraft, _tenant.Value), CancellationToken.None));
        Assert.False(await service.HasPermissionAsync(staff.UserId, IdentityPermission.KitchenHandle, PermissionScope.Kraft(Guid.CreateVersion7(), _tenant.Value), CancellationToken.None));
        Assert.False(await service.HasPermissionAsync(staff.UserId, IdentityPermission.KitchenView, PermissionScope.Tenant(_tenant.Value), CancellationToken.None));
    }

    [Theory]
    [InlineData("Development", true, true)]
    [InlineData("Production", true, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Production", false, true)]
    public void LoggingOtpCodesIsOnlyAllowedInDevelopment(string environment, bool logCodes, bool valid)
    {
        var validator = new IdentityDevelopmentOptionsValidator(new Environment(environment));

        var result = validator.Validate(null, new IdentityDevelopmentOptions { LogOtpCodes = logCodes });

        Assert.Equal(valid, result.Succeeded);
    }

    private static RoleAssignment Grant(IdentityRole role, RoleScopeType scopeType, Guid? scopeId) =>
        RoleAssignment.Grant(
            new RoleAssignmentId(Guid.CreateVersion7()),
            ApplicationUserId.New(),
            role,
            scopeType,
            scopeId,
            DateTimeOffset.UtcNow).Value!;

    private sealed class FixedAssignments(params RoleAssignment[] assignments) : IRoleAssignmentRepository
    {
        public Task<RoleAssignment?> FindAsync(RoleAssignmentId id, CancellationToken cancellationToken) =>
            Task.FromResult(assignments.FirstOrDefault(x => x.Id == id));

        public Task AddAsync(RoleAssignment assignment, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(RoleAssignment assignment, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<RoleAssignment>> GetActiveForUserAsync(ApplicationUserId userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RoleAssignment>>([.. assignments.Where(x => x.UserId == userId)]);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
