using Common.Application.Persistence;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Tenancy.Domain.Tenants;

namespace Identity.Infrastructure.Seeding;

public sealed class IdentitySeeder(
    IdentityDbContext db,
    IHostEnvironment environment)
    : IDatabaseSeeder
{
    private const int SeederPriority = 20;

    private const string DevelopmentEmail =
        "skeptikrafts222@outlook.com";

    private const string DevelopmentDisplayName =
        "Development Admin";

    private const string TermsKey =
        "krafthaus-terms";

    private const int TermsVersion = 1;

    private const string TermsTextHash =
        "development-terms-v1";

    private const string PrivacyKey =
        "krafthaus-privacy";

    private const int PrivacyVersion = 1;

    private const string PrivacyTextHash =
        "development-privacy-v1";

    public int Priority => SeederPriority;

    public async Task SeedAsync(
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        var emailResult = EmailAddress.Create(
            DevelopmentEmail);

        if (emailResult.IsFailure)
        {
            throw new InvalidOperationException(
                emailResult.Error!.Description);
        }

        var email = emailResult.Value!;

        var existingUser = await db.Users
            .SingleOrDefaultAsync(
                x => x.Email == email,
                cancellationToken);

        if (existingUser is not null)
        {
            return;
        }

        var termsResult = PolicyRef.Create(
            TermsKey,
            TermsVersion,
            TermsTextHash);

        if (termsResult.IsFailure)
        {
            throw new InvalidOperationException(
                termsResult.Error!.Description);
        }

        var privacyResult = PolicyRef.Create(
            PrivacyKey,
            PrivacyVersion,
            PrivacyTextHash);

        if (privacyResult.IsFailure)
        {
            throw new InvalidOperationException(
                privacyResult.Error!.Description);
        }

        var result = ApplicationUser.RegisterVerifiedEmail(
            ApplicationUserId.New(),
            IdentityScope.Platform,
            null,
            email,
            DevelopmentDisplayName,
            termsResult.Value!,
            privacyResult.Value!,
            DateTimeOffset.UtcNow);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                result.Error!.Description);
        }

        db.Users.Add(result.Value!);

        await db.SaveChangesAsync(
            cancellationToken);
    }
}
