using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Events;
using Tenancy.Domain.Common;
using Tenancy.Domain.Tenants;

namespace Identity.Domain.Users;

public sealed class ApplicationUser(ApplicationUserId id)
    : AggregateRoot<ApplicationUserId>(id)
{
    private readonly List<ConsentRecord> _consents = [];

    private ApplicationUser()
        : this(ApplicationUserId.New())
    {
    }

    public IdentityScope Scope { get; private set; }

    public TenantId? TenantId { get; private set; }

    public PhoneNumber? Phone { get; private set; }

    public EmailAddress? Email { get; private set; }

    public string DisplayName { get; private set; } = default!;

    public UserStatus Status { get; private set; }

    public DateTimeOffset? LastSignedInAt { get; private set; }

    public IReadOnlyCollection<ConsentRecord> Consents =>
        _consents.AsReadOnly();

    public static Result<ApplicationUser> RegisterVerifiedPhone(
        ApplicationUserId id,
        IdentityScope scope,
        TenantId? tenantId,
        PhoneNumber phone,
        string displayName,
        PolicyRef terms,
        PolicyRef privacy,
        DateTimeOffset now)
    {
        return Register(
            id,
            scope,
            tenantId,
            phone,
            null,
            displayName,
            terms,
            privacy,
            now);
    }

    public static Result<ApplicationUser> RegisterVerifiedEmail(
        ApplicationUserId id,
        IdentityScope scope,
        TenantId? tenantId,
        EmailAddress email,
        string displayName,
        PolicyRef terms,
        PolicyRef privacy,
        DateTimeOffset now)
    {
        return Register(
            id,
            scope,
            tenantId,
            null,
            email,
            displayName,
            terms,
            privacy,
            now);
    }

    private static Result<ApplicationUser> Register(
        ApplicationUserId id,
        IdentityScope scope,
        TenantId? tenantId,
        PhoneNumber? phone,
        EmailAddress? email,
        string displayName,
        PolicyRef terms,
        PolicyRef privacy,
        DateTimeOffset now)
    {
        if (scope == IdentityScope.Tenant &&
            tenantId is null)
        {
            return IdentityErrors.Required(
                nameof(TenantId),
                "Tenant users require a tenant id.");
        }

        if (scope == IdentityScope.Platform &&
            tenantId is not null)
        {
            return IdentityErrors.Invalid(
                nameof(TenantId),
                "Platform users cannot have a tenant id.");
        }

        if (phone is null && email is null)
        {
            return IdentityErrors.Invalid(
                "Identity",
                "A verified phone or email is required.");
        }

        if (phone is not null && email is not null)
        {
            return IdentityErrors.Invalid(
                "Identity",
                "Register with one verified identity channel at a time.");
        }

        if (string.IsNullOrWhiteSpace(displayName) ||
            displayName.Trim().Length is < 1 or > 100)
        {
            return IdentityErrors.Invalid(
                nameof(DisplayName),
                "Display name must be between 1 and 100 characters.");
        }

        var user = new ApplicationUser(id)
        {
            Scope = scope,
            TenantId = tenantId,
            Phone = phone,
            Email = email,
            DisplayName = displayName.Trim(),
            Status = UserStatus.Active
        };

        user._consents.Add(
            ConsentRecord.Create(
                new ConsentRecordId(Guid.CreateVersion7()),
                id,
                ConsentKind.Terms,
                true,
                terms,
                now));

        user._consents.Add(
            ConsentRecord.Create(
                new ConsentRecordId(Guid.CreateVersion7()),
                id,
                ConsentKind.Privacy,
                true,
                privacy,
                now));

        user.RaiseEvent(
            new UserRegistered(
                id,
                scope,
                tenantId?.ToString(),
                phone?.Value,
                email?.Value,
                user.DisplayName));

        user.RaiseEvent(
            new ConsentRecorded(
                id,
                ConsentKind.Terms,
                true,
                terms.Key,
                terms.Version,
                terms.TextHash));

        user.RaiseEvent(
            new ConsentRecorded(
                id,
                ConsentKind.Privacy,
                true,
                privacy.Key,
                privacy.Version,
                privacy.TextHash));

        return user;
    }

    public Result Rename(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) ||
            displayName.Trim().Length is < 1 or > 100)
        {
            return IdentityErrors.Invalid(
                nameof(DisplayName),
                "Display name must be between 1 and 100 characters.");
        }

        var normalized = displayName.Trim();

        if (DisplayName == normalized)
        {
            return Error.SameValue(
                nameof(DisplayName),
                normalized);
        }

        DisplayName = normalized;

        RaiseEvent(
            new UserProfileChanged(
                Id,
                Scope,
                TenantId?.ToString(),
                Phone?.Value,
                Email?.Value,
                DisplayName));

        return Result.Success;
    }

    public Result RecordConsent(
        ConsentKind kind,
        bool granted,
        PolicyRef policy,
        DateTimeOffset now)
    {
        _consents.Add(
            ConsentRecord.Create(
                new ConsentRecordId(Guid.CreateVersion7()),
                Id,
                kind,
                granted,
                policy,
                now));

        RaiseEvent(
            new ConsentRecorded(
                Id,
                kind,
                granted,
                policy.Key,
                policy.Version,
                policy.TextHash));

        return Result.Success;
    }

    public Result RecordSignIn(DateTimeOffset now)
    {
        if (Status == UserStatus.Disabled)
        {
            return IdentityErrors.Unauthorized(
                "UserDisabled",
                "The user account is disabled.");
        }

        LastSignedInAt = now;

        RaiseEvent(
            new UserSignedIn(
                Id,
                now));

        return Result.Success;
    }

    public Result Disable()
    {
        if (Status == UserStatus.Disabled)
        {
            return Error.SameValue(
                nameof(Status),
                Status);
        }

        Status = UserStatus.Disabled;

        RaiseEvent(
            new UserDisabled(Id));

        return Result.Success;
    }

    public Result Enable()
    {
        if (Status == UserStatus.Active)
        {
            return Error.SameValue(
                nameof(Status),
                Status);
        }

        Status = UserStatus.Active;

        RaiseEvent(
            new UserEnabled(Id));

        return Result.Success;
    }

    public bool HasAcceptedVersion(
        ConsentKind kind,
        PolicyRef currentPolicy)
    {
        return _consents.Any(x =>
            x.Kind == kind &&
            x.Granted &&
            x.PolicyKey == currentPolicy.Key &&
            x.PolicyVersion == currentPolicy.Version &&
            x.PolicyTextHash == currentPolicy.TextHash);
    }

    public UserSnapshot ToSnapshot()
    {
        return new UserSnapshot(
            Id,
            Scope,
            TenantId?.ToString(),
            Phone?.Value,
            Email?.Value,
            DisplayName,
            Status);
    }
}
