using Identity.Domain.Authorization;
using Identity.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace Identity.Tests;

public sealed class PermissionPolicyProviderTests
{
    private static PermissionPolicyProvider CreateProvider()
    {
        return new PermissionPolicyProvider(
            Options.Create(
                new AuthorizationOptions()));
    }

    [Fact]
    public async Task PermissionPolicyRequiresAuthenticatedUser()
    {
        var provider =
            CreateProvider();

        var policy =
            await provider.GetPolicyAsync(
                $"{PermissionPolicyProvider.PolicyPrefix}{IdentityPermission.TicketsCheckIn}");

        Assert.NotNull(policy);

        Assert.Contains(
            policy.Requirements,
            requirement =>
                requirement is DenyAnonymousAuthorizationRequirement);

        Assert.Contains(
            policy.Requirements,
            requirement =>
                requirement is PermissionRequirement permission &&
                permission.Permission ==
                IdentityPermission.TicketsCheckIn);
    }

    [Fact]
    public async Task PermissionPolicyCreatesPermissionRequirement()
    {
        var provider =
            CreateProvider();

        var policy =
            await provider.GetPolicyAsync(
                $"{PermissionPolicyProvider.PolicyPrefix}{IdentityPermission.PaymentsRefund}");

        Assert.NotNull(policy);

        var requirement =
            Assert.Single(
                policy.Requirements.OfType<PermissionRequirement>());

        Assert.Equal(
            IdentityPermission.PaymentsRefund,
            requirement.Permission);
    }

    [Fact]
    public async Task UnknownNonPermissionPolicyIsDelegatedToDefaultProvider()
    {
        var provider =
            CreateProvider();

        var policy =
            await provider.GetPolicyAsync(
                "SomeExistingPolicy");

        Assert.Null(policy);
    }

    [Fact]
    public async Task EmptyPermissionPolicyIsNotCreated()
    {
        var provider =
            CreateProvider();

        var policy =
            await provider.GetPolicyAsync(
                PermissionPolicyProvider.PolicyPrefix);

        Assert.Null(policy);
    }
}
