using Common.Application.Persistence;
using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Identity.Infrastructure.Otp;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Seeding;

/// <summary>
///     Development only. Seeds a Tenant Admin (<c>Identity:Development:Admin*</c>) in the identity realm of the
///     development host's tenant, so the whole staff-management flow can be exercised from Postman. Idempotent: an
///     existing user is reused and the role is only granted when missing.
/// </summary>
public sealed class IdentitySeeder(
    IdentityDbContext db,
    IHostEnvironment environment,
    IOptions<IdentityDevelopmentOptions> options,
    ITenantDirectory tenants,
    TimeProvider time)
    : IDatabaseSeeder
{
    private const int SeederPriority = 20;

    private const string TermsKey = "krafthaus-terms";
    private const int TermsVersion = 1;
    private const string TermsTextHash = "development-terms-v1";

    private const string PrivacyKey = "krafthaus-privacy";
    private const int PrivacyVersion = 1;
    private const string PrivacyTextHash = "development-privacy-v1";

    public int Priority => SeederPriority;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!environment.IsDevelopment() ||
            (string.IsNullOrWhiteSpace(settings.AdminEmail) && string.IsNullOrWhiteSpace(settings.AdminPhone)))
        {
            return;
        }

        var tenant = await tenants.FindByHostAsync(settings.AdminHost, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Development admin host '{settings.AdminHost}' is not a registered tenant host. " +
                "Run the Tenancy seeder first or fix Identity:Development:AdminHost.");

        var realm = IdentityRealm.ForHost(tenant.TenantId, tenant.IdentityScope);
        var now = time.GetUtcNow();

        var user = await FindAdminAsync(realm, settings, cancellationToken);

        if (user is null)
        {
            user = Unwrap(RegisterAdmin(realm, settings, now));
            db.Users.Add(user);
        }

        var hasTenantAdmin = await db.RoleAssignments.AnyAsync(
            x => x.UserId == user.Id &&
                 x.Role == IdentityRole.TenantAdmin &&
                 x.ScopeType == RoleScopeType.Tenant &&
                 x.ScopeId == tenant.TenantId.Value &&
                 x.Status == RoleAssignmentStatus.Active,
            cancellationToken);

        if (!hasTenantAdmin)
        {
            db.RoleAssignments.Add(Unwrap(RoleAssignment.Grant(
                new RoleAssignmentId(Guid.CreateVersion7()),
                user.Id,
                IdentityRole.TenantAdmin,
                RoleScopeType.Tenant,
                tenant.TenantId.Value,
                now)));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ApplicationUser?> FindAdminAsync(
        IdentityRealm realm,
        IdentityDevelopmentOptions settings,
        CancellationToken cancellationToken)
    {
        var inRealm = db.Users.Where(x => x.Scope == realm.Scope && x.TenantId == realm.TenantId);

        if (!string.IsNullOrWhiteSpace(settings.AdminEmail))
        {
            var email = Unwrap(EmailAddress.Create(settings.AdminEmail));
            return await inRealm.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        }

        var phone = Unwrap(PhoneNumber.Create(settings.AdminPhone!));
        return await inRealm.SingleOrDefaultAsync(x => x.Phone == phone, cancellationToken);
    }

    private static Result<ApplicationUser> RegisterAdmin(
        IdentityRealm realm,
        IdentityDevelopmentOptions settings,
        DateTimeOffset now)
    {
        var terms = Unwrap(PolicyRef.Create(TermsKey, TermsVersion, TermsTextHash));
        var privacy = Unwrap(PolicyRef.Create(PrivacyKey, PrivacyVersion, PrivacyTextHash));

        return !string.IsNullOrWhiteSpace(settings.AdminEmail)
            ? ApplicationUser.RegisterVerifiedEmail(
                ApplicationUserId.New(),
                realm.Scope,
                realm.TenantId,
                Unwrap(EmailAddress.Create(settings.AdminEmail)),
                settings.AdminDisplayName,
                terms,
                privacy,
                now)
            : ApplicationUser.RegisterVerifiedPhone(
                ApplicationUserId.New(),
                realm.Scope,
                realm.TenantId,
                Unwrap(PhoneNumber.Create(settings.AdminPhone!)),
                settings.AdminDisplayName,
                terms,
                privacy,
                now);
    }

    private static T Unwrap<T>(Result<T> result) =>
        result.IsFailure
            ? throw new InvalidOperationException(result.Error!.Description ?? result.Error.Key)
            : result.Value!;
}
