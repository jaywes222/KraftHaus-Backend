using Identity.Infrastructure.Seeding;
using Xunit;

namespace Identity.Tests;

public sealed class BootstrapOptionsTests
{
    private readonly IdentityBootstrapOptionsValidator _validator = new();

    [Fact]
    public void OffWhenNoHostIsSet() =>
        Assert.True(_validator.Validate(null, new IdentityBootstrapOptions()).Succeeded);

    [Fact]
    public void NeedsExactlyOneOfPhoneOrEmail()
    {
        Assert.False(_validator.Validate(null, new IdentityBootstrapOptions { AdminHost = "api.krafthaus.co.ke" }).Succeeded);
        Assert.False(_validator.Validate(null, new IdentityBootstrapOptions
        {
            AdminHost = "api.krafthaus.co.ke",
            AdminPhone = "0712345678",
            AdminEmail = "admin@krafthaus.co.ke"
        }).Succeeded);
        Assert.True(_validator.Validate(null, new IdentityBootstrapOptions
        {
            AdminHost = "api.krafthaus.co.ke",
            AdminPhone = "0712345678"
        }).Succeeded);
    }

    [Fact]
    public void RejectsAnInvalidPhoneOrEmail()
    {
        Assert.False(_validator.Validate(null, new IdentityBootstrapOptions { AdminHost = "a.test", AdminPhone = "12" }).Succeeded);
        Assert.False(_validator.Validate(null, new IdentityBootstrapOptions { AdminHost = "a.test", AdminEmail = "nope" }).Succeeded);
    }
}
