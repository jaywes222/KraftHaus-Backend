using Common.Domain.Tenancy;
using Tenancy.Domain.Common;
using Tenancy.Domain.Tenants;
using Xunit;

namespace Tenancy.Tests;

public sealed class TenantDomainTests
{
    [Fact]
    public void TenantSlugNormalizesAndRejectsInvalidValues()
    {
        var valid = TenantSlug.Create("  Client-A ");
        var invalid = TenantSlug.Create("Client A");

        Assert.False(valid.IsFailure);
        Assert.Equal("CLIENT-A", valid.Value!.Value);
        Assert.True(invalid.IsFailure);
    }

    [Fact]
    public void HostNameNormalizesAndRejectsNonDnsValues()
    {
        var valid = HostName.Create("Events.Krafthaus.co.ke.");
        var invalid = HostName.Create("not a host");

        Assert.False(valid.IsFailure);
        Assert.Equal("EVENTS.KRAFTHAUS.CO.KE", valid.Value!.Value);
        Assert.True(invalid.IsFailure);
    }

    [Fact]
    public void TenantEnforcesOnePrimaryHost()
    {
        var tenant = Tenant.Create(
            new TenantId(Guid.CreateVersion7()),
            TenantSlug.Create("client-a").Value!,
            "Client A").Value!;

        var first = tenant.AddHost(TenantHost.Create(
            new TenantHostId(Guid.CreateVersion7()),
            tenant.Id,
            HostName.Create("client-a.example.com").Value!,
            HostSurface.Client,
            true));
        var second = tenant.AddHost(TenantHost.Create(
            new TenantHostId(Guid.CreateVersion7()),
            tenant.Id,
            HostName.Create("admin.client-a.example.com").Value!,
            HostSurface.Admin,
            true));

        Assert.False(first.IsFailure);
        Assert.True(second.IsFailure);
    }

    [Fact]
    public void TenantRejectsDuplicateHostName()
    {
        var tenant = Tenant.Create(
            new TenantId(Guid.CreateVersion7()),
            TenantSlug.Create("client-a").Value!,
            "Client A").Value!;
        var hostName = HostName.Create("client-a.example.com").Value!;

        tenant.AddHost(TenantHost.Create(new TenantHostId(Guid.CreateVersion7()), tenant.Id, hostName, HostSurface.Client, true));
        var duplicate = tenant.AddHost(TenantHost.Create(new TenantHostId(Guid.CreateVersion7()), tenant.Id, hostName, HostSurface.Admin, false));

        Assert.True(duplicate.IsFailure);
    }
}
