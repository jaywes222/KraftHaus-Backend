using Common.Application.Commerce;
using Common.Application.Tenancy;
using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Identity.Application.Staff;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Xunit;

namespace Identity.Tests;

/// <summary>UF-022 staff list: which roles count as staff of the host's tenant.</summary>
public sealed class StaffMembersTests
{
    private static readonly TenantId _tenant = new(Guid.CreateVersion7());
    private static readonly TenantId _otherTenant = new(Guid.CreateVersion7());
    private static readonly Guid _ourKraft = Guid.CreateVersion7();
    private static readonly Guid _otherTenantsKraft = Guid.CreateVersion7();

    [Fact]
    public async Task ListsTenantRolesOfThisTenantOnly()
    {
        var amina = ApplicationUserId.New();
        var brian = ApplicationUserId.New();

        var result = await ServiceWith(
            Row(amina, "Amina", IdentityRole.TenantAdmin, RoleScopeType.Tenant, _tenant.Value),
            Row(brian, "Brian", IdentityRole.Finance, RoleScopeType.Tenant, _otherTenant.Value))
            .ExecuteAsync(CancellationToken.None);

        var member = Assert.Single(result.Value!);
        Assert.Equal(amina, member.UserId);
        Assert.Equal(IdentityRole.TenantAdmin, Assert.Single(member.Roles).Role);
    }

    [Fact]
    public async Task KeepsKraftRolesOnlyForKraftsOfThisTenantWithTheirNames()
    {
        var owner = ApplicationUserId.New();
        var elsewhere = ApplicationUserId.New();

        var result = await ServiceWith(
            Row(owner, "Wanjiru", IdentityRole.KraftOwner, RoleScopeType.Kraft, _ourKraft),
            Row(elsewhere, "Otieno", IdentityRole.KraftStaff, RoleScopeType.Kraft, _otherTenantsKraft))
            .ExecuteAsync(CancellationToken.None);

        var member = Assert.Single(result.Value!);
        Assert.Equal(owner, member.UserId);
        var role = Assert.Single(member.Roles);
        Assert.Equal("Grill Kraft", role.ScopeName);
    }

    [Fact]
    public async Task WithoutAKraftDirectoryKraftRolesAreLeftOut()
    {
        var owner = ApplicationUserId.New();
        var service = new ListStaffMembersService(
            new FixedDirectory(Row(owner, "Wanjiru", IdentityRole.KraftOwner, RoleScopeType.Kraft, _ourKraft)),
            [],
            new HostTenant());

        var result = await service.ExecuteAsync(CancellationToken.None);

        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task GroupsRolesPerPersonAndSortsByName()
    {
        var zawadi = ApplicationUserId.New();
        var amina = ApplicationUserId.New();
        var earlier = DateTimeOffset.UtcNow.AddDays(-2);

        var result = await ServiceWith(
            Row(zawadi, "zawadi", IdentityRole.Support, RoleScopeType.Tenant, _tenant.Value),
            Row(amina, "Amina", IdentityRole.Finance, RoleScopeType.Tenant, _tenant.Value),
            Row(amina, "Amina", IdentityRole.KraftOwner, RoleScopeType.Kraft, _ourKraft, earlier))
            .ExecuteAsync(CancellationToken.None);

        Assert.Equal(["Amina", "zawadi"], result.Value!.Select(x => x.DisplayName));
        Assert.Equal(
            [IdentityRole.KraftOwner, IdentityRole.Finance],
            result.Value![0].Roles.Select(x => x.Role));
    }

    [Fact]
    public async Task PlatformRolesAreNotTenantStaff()
    {
        var result = await ServiceWith(
            Row(ApplicationUserId.New(), "Root", IdentityRole.SuperAdmin, RoleScopeType.Platform, null))
            .ExecuteAsync(CancellationToken.None);

        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task RequiresATenantHost()
    {
        var service = new ListStaffMembersService(new FixedDirectory(), [], new NoTenant());

        var result = await service.ExecuteAsync(CancellationToken.None);

        Assert.Equal("Identity.TenantUnresolved", result.Error!.Key);
    }

    private static ListStaffMembersService ServiceWith(params StaffAssignmentRow[] rows) =>
        new(new FixedDirectory(rows), [new OurKrafts()], new HostTenant());

    private static StaffAssignmentRow Row(
        ApplicationUserId userId,
        string name,
        IdentityRole role,
        RoleScopeType scopeType,
        Guid? scopeId,
        DateTimeOffset? grantedAt = null) =>
        new(
            userId,
            name,
            "+254712345678",
            null,
            UserStatus.Active,
            null,
            new RoleAssignmentId(Guid.CreateVersion7()),
            role,
            scopeType,
            scopeId,
            grantedAt ?? DateTimeOffset.UtcNow);

    private sealed class FixedDirectory(params StaffAssignmentRow[] rows) : IStaffDirectory
    {
        public Task<IReadOnlyList<StaffAssignmentRow>> ListActiveAssignmentsAsync(
            IdentityRealm realm,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StaffAssignmentRow>>(rows);
    }

    /// <summary>This tenant's Commerce data: one Kraft.</summary>
    private sealed class OurKrafts : IKraftDirectory
    {
        public Task<KraftAtEvent?> FindAtEventAsync(Guid kraftId, Guid eventId, CancellationToken cancellationToken) =>
            Task.FromResult<KraftAtEvent?>(null);

        public Task<IReadOnlyDictionary<Guid, string>> NamesAsync(
            IReadOnlyCollection<Guid> kraftIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                kraftIds.Where(x => x == _ourKraft).ToDictionary(x => x, _ => "Grill Kraft"));
    }

    private sealed class HostTenant : ICurrentTenant
    {
        public bool IsResolved => true;

        public TenantId TenantId => _tenant;

        public IdentityScope IdentityScope => IdentityScope.Platform;
    }

    private sealed class NoTenant : ICurrentTenant
    {
        public bool IsResolved => false;

        public TenantId TenantId => throw new InvalidOperationException("No tenant.");

        public IdentityScope IdentityScope => IdentityScope.Platform;
    }
}
