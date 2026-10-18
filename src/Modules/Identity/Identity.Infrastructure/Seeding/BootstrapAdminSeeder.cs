using Common.Application.Persistence;
using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Options;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Seeding;

/// <summary>
///     <c>Identity:Bootstrap</c>. The first Tenant Admin of a tenant on a fresh server, so somebody can sign in and
///     invite everyone else. Works in every environment, and only while the tenant has no active Tenant Admin.
/// </summary>
public sealed class IdentityBootstrapOptions
{
    public const string SectionName = "Identity:Bootstrap";

    /// <summary>A registered host of the tenant (the API host, e.g. <c>api.krafthaus.co.ke</c>). Empty: off.</summary>
    public string? AdminHost { get; init; }

    /// <summary>The admin signs in with this phone (SMS OTP)…</summary>
    public string? AdminPhone { get; init; }

    /// <summary>…or this e-mail (e-mail OTP). Exactly one of the two.</summary>
    public string? AdminEmail { get; init; }

    public string AdminDisplayName { get; init; } = "Tenant Admin";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AdminHost);
}

public sealed class IdentityBootstrapOptionsValidator : IValidateOptions<IdentityBootstrapOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityBootstrapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsConfigured)
        {
            return ValidateOptionsResult.Success;
        }

        var hasPhone = !string.IsNullOrWhiteSpace(options.AdminPhone);
        var hasEmail = !string.IsNullOrWhiteSpace(options.AdminEmail);
        var failures = new List<string>();

        if (hasPhone == hasEmail)
        {
            failures.Add($"{IdentityBootstrapOptions.SectionName}: set exactly one of AdminPhone or AdminEmail.");
        }

        if (hasPhone && PhoneNumber.Create(options.AdminPhone!).IsFailure)
        {
            failures.Add($"{IdentityBootstrapOptions.SectionName}:AdminPhone is not a valid mobile number.");
        }

        if (hasEmail && EmailAddress.Create(options.AdminEmail!).IsFailure)
        {
            failures.Add($"{IdentityBootstrapOptions.SectionName}:AdminEmail is not a valid e-mail address.");
        }

        if (string.IsNullOrWhiteSpace(options.AdminDisplayName))
        {
            failures.Add($"{IdentityBootstrapOptions.SectionName}:AdminDisplayName must not be empty.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
///     Grants Tenant Admin to the configured person when the tenant behind <see cref="IdentityBootstrapOptions.AdminHost" />
///     has none. Once any Tenant Admin exists it does nothing, so a stale setting can never re-grant a role that was
///     revoked. The account records acceptance of the configured terms and privacy versions, like any sign-up.
/// </summary>
public sealed partial class BootstrapAdminSeeder(
    IdentityDbContext db,
    IOptions<IdentityBootstrapOptions> options,
    IOptions<IdentityPolicyOptions> policies,
    ITenantDirectory tenants,
    TimeProvider time,
    ILogger<BootstrapAdminSeeder> logger)
    : IDatabaseSeeder
{
    private const int SeederPriority = 21;

    public int Priority => SeederPriority;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!settings.IsConfigured)
        {
            return;
        }

        var tenant = await tenants.FindByHostAsync(settings.AdminHost!, cancellationToken);

        if (tenant is null)
        {
            LogUnknownHost(logger, settings.AdminHost!);
            return;
        }

        var tenantHasAdmin = await db.RoleAssignments.AnyAsync(
            x => x.Role == IdentityRole.TenantAdmin &&
                 x.ScopeType == RoleScopeType.Tenant &&
                 x.ScopeId == tenant.TenantId.Value &&
                 x.Status == RoleAssignmentStatus.Active,
            cancellationToken);

        if (tenantHasAdmin)
        {
            LogAlreadyHasAdmin(logger, settings.AdminHost!);
            return;
        }

        var realm = IdentityRealm.ForHost(tenant.TenantId, tenant.IdentityScope);
        var now = time.GetUtcNow();
        var user = await FindAsync(realm, settings, cancellationToken);

        if (user is null)
        {
            user = Unwrap(Register(realm, settings, now));
            db.Users.Add(user);
        }

        db.RoleAssignments.Add(Unwrap(RoleAssignment.Grant(
            new RoleAssignmentId(Guid.CreateVersion7(now)),
            user.Id,
            IdentityRole.TenantAdmin,
            RoleScopeType.Tenant,
            tenant.TenantId.Value,
            now)));

        await db.SaveChangesAsync(cancellationToken);

        LogGranted(logger, settings.AdminHost!);
    }

    private Task<ApplicationUser?> FindAsync(
        IdentityRealm realm,
        IdentityBootstrapOptions settings,
        CancellationToken cancellationToken)
    {
        var inRealm = db.Users.Where(x => x.Scope == realm.Scope && x.TenantId == realm.TenantId);

        if (!string.IsNullOrWhiteSpace(settings.AdminEmail))
        {
            var email = Unwrap(EmailAddress.Create(settings.AdminEmail));
            return inRealm.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        }

        var phone = Unwrap(PhoneNumber.Create(settings.AdminPhone!));
        return inRealm.SingleOrDefaultAsync(x => x.Phone == phone, cancellationToken);
    }

    private Result<ApplicationUser> Register(IdentityRealm realm, IdentityBootstrapOptions settings, DateTimeOffset now)
    {
        var configured = policies.Value;
        var terms = Unwrap(PolicyRef.Create(configured.Terms.Key, configured.Terms.Version, configured.Terms.TextHash));
        var privacy = Unwrap(PolicyRef.Create(configured.Privacy.Key, configured.Privacy.Version, configured.Privacy.TextHash));

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

    [LoggerMessage(EventId = 9101, Level = LogLevel.Error,
        Message = "Identity:Bootstrap: host {Host} is not a registered tenant host. Run the Migrator's register-tenant first.")]
    private static partial void LogUnknownHost(ILogger logger, string host);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Information,
        Message = "Identity:Bootstrap: the tenant behind {Host} already has a Tenant Admin; nothing to do. Remove the setting.")]
    private static partial void LogAlreadyHasAdmin(ILogger logger, string host);

    [LoggerMessage(EventId = 9103, Level = LogLevel.Warning,
        Message = "Identity:Bootstrap: granted the first Tenant Admin of the tenant behind {Host}. Remove the setting now.")]
    private static partial void LogGranted(ILogger logger, string host);
}
