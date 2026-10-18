using System.Globalization;
using Common.Application.Tenancy;
using Common.Domain.Codes;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Abstractions;
using Identity.Application.Staff;
using Identity.Domain.Badges;
using Identity.Domain.Common;
using Identity.Domain.Users;
using Microsoft.Extensions.Options;

namespace Identity.Application.Badges;

/// <summary>
///     Printable staff badges: photo, name, title, roles and a QR that opens the public check page. Only people who hold
///     a staff role on the host's tenant get one; the check page says "not valid" as soon as their roles are revoked,
///     their account is disabled or the badge is reissued.
/// </summary>
public sealed class StaffBadgeService(
    IStaffBadgeRepository badges,
    IStaffPhotoStorage photos,
    ListStaffMembersService staff,
    IIdentityUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    IOptions<StaffBadgeOptions> options,
    TimeProvider time)
{
    private const int MaxCodeAttempts = 5;

    public async Task<Result<StaffBadgeView>> GetAsync(ApplicationUserId userId, CancellationToken cancellationToken)
    {
        var member = await MemberAsync(userId, cancellationToken);
        if (member.IsFailure)
        {
            return member.Error!;
        }

        var badge = await badges.FindForUserAsync(currentTenant.TenantId, userId, cancellationToken);

        return View(member.Value!, badge);
    }

    /// <summary>Issues the badge, or with <c>reissue</c> gives it a new QR code (old printouts stop checking out).</summary>
    public async Task<Result<StaffBadgeView>> IssueAsync(ApplicationUserId userId, IssueStaffBadgeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var member = await MemberAsync(userId, cancellationToken);
        if (member.IsFailure)
        {
            return member.Error!;
        }

        var now = time.GetUtcNow();
        var badge = await badges.FindForUserAsync(currentTenant.TenantId, userId, cancellationToken);

        if (badge is null)
        {
            badge = StaffBadge.Issue(new StaffBadgeId(Guid.CreateVersion7(now)), currentTenant.TenantId, userId, await NewCodeAsync(cancellationToken), now);
            badges.Add(badge);
        }
        else if (request.Reissue)
        {
            badge.Reissue(await NewCodeAsync(cancellationToken), now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return View(member.Value!, badge);
    }

    public async Task<Result<StaffBadgeView>> UpdateAsync(ApplicationUserId userId, UpdateStaffBadgeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issued = await IssuedBadgeAsync(userId, cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error!;
        }

        var (member, badge) = issued.Value;
        var changed = badge.ChangeTitle(request.Title);
        if (changed.IsFailure)
        {
            return changed.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return View(member, badge);
    }

    /// <summary>Stores a JPEG, PNG or WebP photo (checked by its bytes, not its name) and replaces the old one.</summary>
    public async Task<Result<StaffBadgeView>> UploadPhotoAsync(ApplicationUserId userId, Stream content, long length, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var max = options.Value.MaxPhotoBytes;
        if (length <= 0 || length > max)
        {
            return IdentityErrors.Invalid("Photo", $"Choose a photo under {max / (1024 * 1024)} MB.");
        }

        var issued = await IssuedBadgeAsync(userId, cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error!;
        }

        var (member, badge) = issued.Value;

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > max)
        {
            return IdentityErrors.Invalid("Photo", $"Choose a photo under {max / (1024 * 1024)} MB.");
        }

        var bytes = buffer.ToArray();
        if (Sniff(bytes) is not { } format)
        {
            return IdentityErrors.Invalid("Photo", "Use a JPEG, PNG or WebP photo.");
        }

        var path = await photos.SaveAsync(badge.TenantId, badge.Id.Value, badge.PhotoVersion + 1, format.Extension, bytes, cancellationToken);
        var previous = badge.ReplacePhoto(path, format.ContentType);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (previous is not null && previous != path)
        {
            photos.Delete(previous);
        }

        return View(member, badge);
    }

    /// <summary>Public: what a scanned badge says. Unknown codes and badges from another tenant are simply not valid.</summary>
    public async Task<Result<StaffBadgeCheckView>> CheckAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = ShortCode.Normalize(code);
        var badge = normalized.Length == StaffBadge.CodeLength ? await badges.FindByCodeForReadAsync(normalized, cancellationToken) : null;

        if (badge is null || !currentTenant.IsResolved || badge.TenantId != currentTenant.TenantId)
        {
            return new StaffBadgeCheckView(false, null, null, [], null);
        }

        var member = await MemberAsync(badge.UserId, cancellationToken);

        return member.IsFailure || member.Value!.Status != UserStatus.Active
            ? new StaffBadgeCheckView(false, null, null, [], null)
            : new StaffBadgeCheckView(true, member.Value.DisplayName, badge.Title, Roles(member.Value), PhotoHref(badge));
    }

    /// <summary>The photo behind a badge code (public, like the check page).</summary>
    public async Task<(Stream Content, string ContentType)?> OpenPhotoAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = ShortCode.Normalize(code);
        var badge = normalized.Length == StaffBadge.CodeLength ? await badges.FindByCodeForReadAsync(normalized, cancellationToken) : null;

        if (badge is not { PhotoPath: { } path, PhotoContentType: { } type } ||
            !currentTenant.IsResolved || badge.TenantId != currentTenant.TenantId)
        {
            return null;
        }

        return photos.OpenRead(path) is { } stream ? (stream, type) : null;
    }

    private async Task<Result<(StaffMemberResult Member, StaffBadge Badge)>> IssuedBadgeAsync(ApplicationUserId userId, CancellationToken cancellationToken)
    {
        var member = await MemberAsync(userId, cancellationToken);
        if (member.IsFailure)
        {
            return member.Error!;
        }

        var badge = await badges.FindForUserAsync(currentTenant.TenantId, userId, cancellationToken);

        return badge is null
            ? IdentityErrors.InvalidState("BadgeNotIssued", "Issue the badge first.")
            : (member.Value!, badge);
    }

    /// <summary>The person must hold a staff role on the host's tenant (the staff list decides).</summary>
    private async Task<Result<StaffMemberResult>> MemberAsync(ApplicationUserId userId, CancellationToken cancellationToken)
    {
        var list = await staff.ExecuteAsync(cancellationToken);
        if (list.IsFailure)
        {
            return list.Error!;
        }

        return list.Value!.FirstOrDefault(x => x.UserId == userId) is { } member
            ? member
            : Error.NotFound("UserId", userId.Value);
    }

    private static StaffBadgeView View(StaffMemberResult member, StaffBadge? badge) =>
        new(
            member.UserId,
            member.DisplayName,
            badge?.Title,
            Roles(member),
            badge?.Code,
            badge is null ? null : PhotoHref(badge),
            badge?.IssuedAt,
            badge?.TimesReissued ?? 0);

    private static IReadOnlyList<StaffBadgeRoleView> Roles(StaffMemberResult member) =>
        [.. member.Roles.Select(x => new StaffBadgeRoleView(x.Role, x.ScopeType, x.ScopeName))];

    private static string? PhotoHref(StaffBadge badge) =>
        badge.PhotoPath is null ? null : $"/api/identity/staff/badges/{badge.Code}/photo?v={badge.PhotoVersion.ToString(CultureInfo.InvariantCulture)}";

    private async Task<string> NewCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxCodeAttempts; attempt++)
        {
            var code = ShortCode.New(StaffBadge.CodeLength);
            if (!await badges.CodeExistsAsync(code, cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not generate a unique badge code.");
    }

    private static (string Extension, string ContentType)? Sniff(byte[] bytes)
    {
        if (bytes is [0xFF, 0xD8, 0xFF, ..])
        {
            return (".jpg", "image/jpeg");
        }

        if (bytes is [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..])
        {
            return (".png", "image/png");
        }

        if (bytes.Length > 12 && bytes is [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..])
        {
            return (".webp", "image/webp");
        }

        return null;
    }
}
