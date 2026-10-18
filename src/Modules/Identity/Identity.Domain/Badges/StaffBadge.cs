using Common.Domain.Aggregates;
using Common.Domain.Events;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Identity.Domain.Common;

namespace Identity.Domain.Badges;

/// <summary>
///     A staff member's printable badge on one tenant: photo, optional title and the secret code behind the badge QR.
///     Scanning the QR opens the public check page, which says whether the person is still staff here. Reissuing gives a
///     new code, so a lost badge stops checking out.
/// </summary>
public sealed class StaffBadge : AggregateRoot<StaffBadgeId>
{
    public const int CodeLength = 16;
    public const int TitleMaxLength = 60;
    public const int PhotoPathMaxLength = 260;

    private StaffBadge(StaffBadgeId id)
        : base(id)
    {
    }

    // For EF Core materialization (the id is set from the row).
    private StaffBadge()
        : this(new StaffBadgeId(Guid.Empty))
    {
    }

    public TenantId TenantId { get; private set; }

    public ApplicationUserId UserId { get; private set; }

    /// <summary>Secret, unguessable code in the QR (Crockford-style alphabet, upper case).</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Shown under the name, e.g. "Head of Security". Optional; the roles are always shown.</summary>
    public string? Title { get; private set; }

    /// <summary>Storage path of the photo (from ids only, never from an uploaded name).</summary>
    public string? PhotoPath { get; private set; }

    public string? PhotoContentType { get; private set; }

    /// <summary>Bumped on every photo change so clients can cache by URL.</summary>
    public int PhotoVersion { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public int TimesReissued { get; private set; }

    public static StaffBadge Issue(StaffBadgeId id, TenantId tenantId, ApplicationUserId userId, string code, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var badge = new StaffBadge(id)
        {
            TenantId = tenantId,
            UserId = userId,
            Code = code,
            IssuedAt = now
        };

        badge.RaiseEvent(new StaffBadgeIssued(id, userId, false));

        return badge;
    }

    /// <summary>New QR code: printed copies of the old badge stop checking out.</summary>
    public void Reissue(string code, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var @event = new StaffBadgeIssued(Id, UserId, true);

        Code = code;
        IssuedAt = now;
        TimesReissued++;

        RaiseEvent(@event);
    }

    public Result ChangeTitle(string? title)
    {
        var trimmed = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        if (trimmed is { Length: > TitleMaxLength })
        {
            return IdentityErrors.Invalid("Title", $"Keep the badge title under {TitleMaxLength} characters.");
        }

        if (trimmed == Title)
        {
            return Result.Success;
        }

        var @event = new StaffBadgeTitleChanged(Id, UserId);

        Title = trimmed;

        RaiseEvent(@event);

        return Result.Success;
    }

    /// <summary>Records a new photo; returns the previous path so the caller can delete the old file.</summary>
    public string? ReplacePhoto(string path, string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        var @event = new StaffBadgePhotoChanged(Id, UserId);
        var previous = PhotoPath;

        PhotoPath = path;
        PhotoContentType = contentType;
        PhotoVersion++;

        RaiseEvent(@event);

        return previous;
    }
}

public sealed record StaffBadgeIssued(StaffBadgeId BadgeId, ApplicationUserId UserId, bool Reissued) : DomainEvent;

public sealed record StaffBadgeTitleChanged(StaffBadgeId BadgeId, ApplicationUserId UserId) : DomainEvent;

public sealed record StaffBadgePhotoChanged(StaffBadgeId BadgeId, ApplicationUserId UserId) : DomainEvent;
