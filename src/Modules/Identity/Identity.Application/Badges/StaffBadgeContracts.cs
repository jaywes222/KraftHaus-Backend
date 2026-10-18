using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Identity.Domain.Badges;
using Identity.Domain.Roles;
using Microsoft.Extensions.Options;

namespace Identity.Application.Badges;

public interface IStaffBadgeRepository
{
    Task<StaffBadge?> FindForUserAsync(TenantId tenantId, ApplicationUserId userId, CancellationToken cancellationToken);

    Task<StaffBadge?> FindByCodeForReadAsync(string code, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    void Add(StaffBadge badge);
}

/// <summary>Staff photos on disk, one folder per tenant. Paths are built from ids only.</summary>
public interface IStaffPhotoStorage
{
    Task<string> SaveAsync(TenantId tenantId, Guid badgeId, int version, string extension, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    Stream? OpenRead(string relativePath);

    void Delete(string relativePath);
}

/// <summary><c>Identity:StaffBadges</c>.</summary>
public sealed class StaffBadgeOptions
{
    public const string SectionName = "Identity:StaffBadges";

    /// <summary>Folder for staff photos; relative paths are under the API's content root. Back it up with the databases.</summary>
    public string PhotosRootPath { get; init; } = "App_Data/staff-photos";

    /// <summary>Largest photo accepted, in bytes.</summary>
    public int MaxPhotoBytes { get; init; } = 3 * 1024 * 1024;
}

public sealed class StaffBadgeOptionsValidator : IValidateOptions<StaffBadgeOptions>
{
    public ValidateOptionsResult Validate(string? name, StaffBadgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.PhotosRootPath))
        {
            failures.Add("Identity:StaffBadges:PhotosRootPath is required.");
        }

        if (options.MaxPhotoBytes is < 50 * 1024 or > 20 * 1024 * 1024)
        {
            failures.Add("Identity:StaffBadges:MaxPhotoBytes must be between 51200 and 20971520.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public sealed record StaffBadgeRoleView(IdentityRole Role, RoleScopeType ScopeType, string? ScopeName);

/// <summary>A staff member's badge as admins (and the member) see it, for printing.</summary>
/// <param name="Code">The QR code; null until a badge is issued.</param>
/// <param name="PhotoHref">API path of the photo (by badge code), with a version for caching; null without a photo.</param>
public sealed record StaffBadgeView(
    ApplicationUserId UserId,
    string DisplayName,
    string? Title,
    IReadOnlyList<StaffBadgeRoleView> Roles,
    string? Code,
    string? PhotoHref,
    DateTimeOffset? IssuedAt,
    int TimesReissued);

/// <summary>What the public check page shows when the badge QR is scanned.</summary>
/// <param name="Valid">The badge is current and the person still holds a staff role here.</param>
public sealed record StaffBadgeCheckView(
    bool Valid,
    string? DisplayName,
    string? Title,
    IReadOnlyList<StaffBadgeRoleView> Roles,
    string? PhotoHref);

public sealed record IssueStaffBadgeRequest(bool Reissue);

public sealed record UpdateStaffBadgeRequest(string? Title);
