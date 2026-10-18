using Common.Domain.Aggregates;
using Displays.Domain.Common;

namespace Displays.Domain.Assets;

/// <summary>
///     An image uploaded for announcement and sponsor slides, stored on the server's disk (<see cref="StoragePath" /> is
///     relative to the configured root). Only JPEG, PNG and WebP are accepted, checked by content, not by name.
/// </summary>
public sealed class DisplayAsset : AggregateRoot<DisplayAssetId>
{
    public const int FileNameMaxLength = 120;
    public const int ContentTypeMaxLength = 32;
    public const int StoragePathMaxLength = 200;

    private DisplayAsset(DisplayAssetId id)
        : base(id)
    {
    }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>SHA-256 of the bytes (hex): the HTTP ETag.</summary>
    public string Sha256 { get; private set; } = string.Empty;

    public string StoragePath { get; private set; } = string.Empty;

    public DateTimeOffset UploadedAt { get; private set; }

    public static DisplayAsset Upload(
        DisplayAssetId id,
        string fileName,
        string contentType,
        long sizeBytes,
        string sha256,
        string storagePath,
        DateTimeOffset now)
    {
        var asset = new DisplayAsset(id)
        {
            FileName = Fit(fileName),
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Sha256 = sha256,
            StoragePath = storagePath,
            UploadedAt = now
        };

        asset.RaiseEvent(new DisplayAssetUploaded(id, contentType, sizeBytes));

        return asset;
    }

    public void MarkDeleted() => RaiseEvent(new DisplayAssetDeleted(Id));

    private static string Fit(string fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            name = "image";
        }

        return name.Length <= FileNameMaxLength ? name : name[..FileNameMaxLength];
    }
}
