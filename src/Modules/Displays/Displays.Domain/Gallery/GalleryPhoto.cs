using Common.Domain.Aggregates;
using Common.Domain.Events;
using Common.Domain.ResultMonad;
using Displays.Domain.Common;

namespace Displays.Domain.Gallery;

/// <summary>A photo from an event in the public gallery: an uploaded image (display asset) with a caption.</summary>
public sealed class GalleryPhoto : AggregateRoot<GalleryPhotoId>
{
    public const int CaptionMaxLength = 140;

    private GalleryPhoto(GalleryPhotoId id)
        : base(id)
    {
    }

    public Guid EventId { get; private set; }

    public Guid AssetId { get; private set; }

    public string? Caption { get; private set; }

    public int SortOrder { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public static Result<GalleryPhoto> Add(GalleryPhotoId id, Guid eventId, Guid assetId, string? caption, int sortOrder, DateTimeOffset now)
    {
        var checkedCaption = Check(caption, sortOrder);
        if (checkedCaption.IsFailure)
        {
            return checkedCaption.Error!;
        }

        var photo = new GalleryPhoto(id)
        {
            EventId = eventId,
            AssetId = assetId,
            Caption = checkedCaption.Value,
            SortOrder = sortOrder,
            AddedAt = now
        };

        photo.RaiseEvent(new GalleryPhotoAdded(id, eventId, assetId));

        return photo;
    }

    public Result Change(string? caption, int sortOrder)
    {
        var checkedCaption = Check(caption, sortOrder);
        if (checkedCaption.IsFailure)
        {
            return checkedCaption.Error!;
        }

        var @event = new GalleryPhotoChanged(Id);
        Caption = checkedCaption.Value;
        SortOrder = sortOrder;
        RaiseEvent(@event);

        return Result.Success;
    }

    public void MarkRemoved() => RaiseEvent(new GalleryPhotoRemoved(Id, EventId));

    private static Result<string?> Check(string? caption, int sortOrder)
    {
        var errors = new List<string>();
        var trimmed = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();

        if (trimmed is { Length: > CaptionMaxLength })
        {
            errors.Add($"Caption: Keep it under {CaptionMaxLength} characters.");
        }

        if (sortOrder is < 0 or > 100_000)
        {
            errors.Add("SortOrder: Use 0-100000.");
        }

        return errors.Count > 0 ? DisplaysErrors.Invalid("GalleryPhotoInvalid", errors) : trimmed;
    }
}

public sealed record GalleryPhotoAdded(GalleryPhotoId PhotoId, Guid EventId, Guid AssetId) : DomainEvent;

public sealed record GalleryPhotoChanged(GalleryPhotoId PhotoId) : DomainEvent;

public sealed record GalleryPhotoRemoved(GalleryPhotoId PhotoId, Guid EventId) : DomainEvent;
