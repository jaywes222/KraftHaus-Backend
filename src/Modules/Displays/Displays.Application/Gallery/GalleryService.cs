using Common.Application.EventCatalog;
using Common.Domain.ResultMonad;
using Displays.Application.Abstractions;
using Displays.Application.Assets;
using Displays.Domain.Common;
using Displays.Domain.Gallery;

namespace Displays.Application.Gallery;

public interface IGalleryRepository
{
    Task<GalleryPhoto?> FindAsync(GalleryPhotoId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<GalleryPhoto>> ListForEventAsync(Guid eventId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GalleryPhoto>> ListAllAsync(int limit, CancellationToken cancellationToken);

    Task<bool> UsesAssetAsync(Guid assetId, CancellationToken cancellationToken);

    void Add(GalleryPhoto photo);

    void Remove(GalleryPhoto photo);
}

public sealed record GalleryPhotoView(Guid Id, Guid EventId, Guid AssetId, Uri ImageUrl, string? Caption, int SortOrder, DateTimeOffset AddedAt)
{
    public static GalleryPhotoView From(GalleryPhoto photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        return new GalleryPhotoView(photo.Id.Value, photo.EventId, photo.AssetId, AssetUrls.For(photo.AssetId), photo.Caption, photo.SortOrder, photo.AddedAt);
    }
}

public sealed record AddGalleryPhotoRequest(Guid AssetId, string? Caption, int? SortOrder);

public sealed record ChangeGalleryPhotoRequest(string? Caption, int SortOrder);

/// <summary>One past (or current) event in the public gallery, newest first.</summary>
public sealed record GalleryEventView(Guid EventId, string Title, string Slug, DateTimeOffset? Date, IReadOnlyList<GalleryPhotoView> Photos);

/// <summary>Photos of events for the public gallery. Images are display assets (uploaded once, JPEG/PNG/WebP).</summary>
public sealed class GalleryService(
    IGalleryRepository gallery,
    IDisplayAssetRepository assets,
    IDisplaysUnitOfWork unitOfWork,
    IEventCatalog events,
    TimeProvider time)
{
    public const int MaxPhotosShown = 1000;

    public async Task<Result<IReadOnlyList<GalleryPhotoView>>> ListAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var list = await gallery.ListForEventAsync(eventId, cancellationToken);
        return Result<IReadOnlyList<GalleryPhotoView>>.Success([.. list.Select(GalleryPhotoView.From)]);
    }

    public async Task<Result<GalleryPhotoView>> AddAsync(Guid eventId, AddGalleryPhotoRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await events.GetSaleSnapshotAsync(eventId, cancellationToken) is null)
        {
            return DisplaysErrors.EventNotFound;
        }

        if (await assets.FindForReadAsync(new DisplayAssetId(request.AssetId), cancellationToken) is null)
        {
            return DisplaysErrors.AssetNotFound;
        }

        var existing = await gallery.ListForEventAsync(eventId, cancellationToken);
        var now = time.GetUtcNow();
        var added = GalleryPhoto.Add(
            new GalleryPhotoId(Guid.CreateVersion7(now)),
            eventId,
            request.AssetId,
            request.Caption,
            request.SortOrder ?? (existing.Count == 0 ? 0 : existing.Max(x => x.SortOrder) + 1),
            now);
        if (added.IsFailure)
        {
            return added.Error!;
        }

        gallery.Add(added.Value!);
        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : GalleryPhotoView.From(added.Value!);
    }

    public async Task<Result<GalleryPhotoView>> ChangeAsync(Guid photoId, ChangeGalleryPhotoRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var photo = await gallery.FindAsync(new GalleryPhotoId(photoId), cancellationToken);
        if (photo is null)
        {
            return PhotoNotFound;
        }

        var changed = photo.Change(request.Caption, request.SortOrder);
        if (changed.IsFailure)
        {
            return changed.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : GalleryPhotoView.From(photo);
    }

    /// <summary>Takes the photo out of the gallery; the image itself stays in the image library.</summary>
    public async Task<Result> RemoveAsync(Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await gallery.FindAsync(new GalleryPhotoId(photoId), cancellationToken);
        if (photo is null)
        {
            return PhotoNotFound;
        }

        photo.MarkRemoved();
        gallery.Remove(photo);
        return await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Public: every event with photos, newest event first.</summary>
    public async Task<Result<IReadOnlyList<GalleryEventView>>> PublicAsync(CancellationToken cancellationToken)
    {
        var photos = await gallery.ListAllAsync(MaxPhotosShown, cancellationToken);
        var result = new List<GalleryEventView>();

        foreach (var group in photos.GroupBy(x => x.EventId))
        {
            var sale = await events.GetSaleSnapshotAsync(group.Key, cancellationToken);
            if (sale is null || sale.Status == EventLifecycle.Draft)
            {
                continue;
            }

            var date = sale.Dates.Count > 0 ? sale.Dates.Min(x => x.StartsAt) : (DateTimeOffset?)null;
            result.Add(new GalleryEventView(
                sale.EventId,
                sale.Title,
                sale.Slug,
                date,
                [.. group.OrderBy(x => x.SortOrder).ThenBy(x => x.AddedAt).Select(GalleryPhotoView.From)]));
        }

        return Result<IReadOnlyList<GalleryEventView>>.Success([.. result.OrderByDescending(x => x.Date)]);
    }

    private static Error PhotoNotFound => DisplaysErrors.NotFound("GalleryPhotoNotFound", "The gallery photo was not found.");
}
