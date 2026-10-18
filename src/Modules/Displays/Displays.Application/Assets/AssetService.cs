using System.Security.Cryptography;
using Common.Domain.ResultMonad;
using Displays.Application.Abstractions;
using Displays.Domain.Assets;
using Displays.Domain.Common;
using Microsoft.Extensions.Options;

namespace Displays.Application.Assets;

public sealed record DisplayAssetView(Guid Id, string FileName, string ContentType, long SizeBytes, Uri Url, DateTimeOffset UploadedAt)
{
    public static DisplayAssetView From(DisplayAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        return new DisplayAssetView(asset.Id.Value, asset.FileName, asset.ContentType, asset.SizeBytes, AssetUrls.For(asset.Id.Value), asset.UploadedAt);
    }
}

/// <summary>An image ready to send: open stream, type and ETag.</summary>
public sealed record AssetContent(Stream Content, string ContentType, string ETag);

public static class AssetUrls
{
    public const string PathPrefix = "/api/public/display-assets/";

    public static Uri For(Guid assetId) => new($"{PathPrefix}{assetId:D}", UriKind.Relative);
}

/// <summary>
///     Images for announcement and sponsor slides, stored on the server's disk. Only JPEG, PNG and WebP, recognised
///     from the file's first bytes (never trusting its name or declared type), so no SVG or HTML can be served back.
/// </summary>
public sealed class AssetService(
    IDisplayAssetRepository assets,
    IDisplayConfigurationRepository configurations,
    Gallery.IGalleryRepository gallery,
    IAssetStorage storage,
    IDisplaysUnitOfWork unitOfWork,
    IOptions<DisplaysOptions> options,
    TimeProvider time)
{
    public async Task<Result<DisplayAssetView>> UploadAsync(string? fileName, Stream? content, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return DisplaysErrors.Invalid("AssetInvalid", ["File: Choose an image to upload."]);
        }

        var max = options.Value.MaxAssetBytes;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > max)
            {
                return DisplaysErrors.Invalid("AssetInvalid", [$"File: Images can be at most {max / 1024 / 1024.0:0.#} MB."]);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
        {
            return DisplaysErrors.Invalid("AssetInvalid", ["File: The file is empty."]);
        }

        if (ImageSniffer.Detect(bytes) is not { } type)
        {
            return DisplaysErrors.Invalid("AssetInvalid", ["File: Use a JPEG, PNG or WebP image."]);
        }

        var now = time.GetUtcNow();
        var id = new DisplayAssetId(Guid.CreateVersion7(now));
        var path = await storage.SaveAsync(id.Value, type.Extension, bytes, cancellationToken);

        var asset = DisplayAsset.Upload(
            id,
            fileName ?? "image" + type.Extension,
            type.ContentType,
            bytes.Length,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            path,
            now);
        assets.Add(asset);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            storage.Delete(path);
            return saved.Error!;
        }

        return DisplayAssetView.From(asset);
    }

    public async Task<Result<IReadOnlyList<DisplayAssetView>>> ListAsync(CancellationToken cancellationToken)
    {
        var list = await assets.ListAsync(500, cancellationToken);

        return Result<IReadOnlyList<DisplayAssetView>>.Success([.. list.Select(DisplayAssetView.From)]);
    }

    /// <summary>Refused while an active configuration shows the image (a slide would go blank).</summary>
    public async Task<Result<DisplayAssetView>> DeleteAsync(Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await assets.FindAsync(new DisplayAssetId(assetId), cancellationToken);
        if (asset is null)
        {
            return DisplaysErrors.AssetNotFound;
        }

        var users = (await configurations.ListAsync(null, includeArchived: false, cancellationToken))
            .Where(x => x.UsesAsset(assetId))
            .Select(x => x.Name)
            .ToList();
        if (await gallery.UsesAssetAsync(assetId, cancellationToken))
        {
            users.Add("the event gallery");
        }

        if (users.Count > 0)
        {
            return DisplaysErrors.Conflict("AssetInUse", $"Configurations still show this image: {string.Join(", ", users)}.");
        }

        asset.MarkDeleted();
        assets.Remove(asset);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        storage.Delete(asset.StoragePath);

        return DisplayAssetView.From(asset);
    }

    public async Task<Result<AssetContent>> OpenAsync(Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await assets.FindForReadAsync(new DisplayAssetId(assetId), cancellationToken);
        var stream = asset is null ? null : storage.OpenRead(asset.StoragePath);

        return stream is null ? DisplaysErrors.AssetNotFound : new AssetContent(stream, asset!.ContentType, $"\"{asset.Sha256}\"");
    }
}

/// <summary>Recognises the image types screens may show from their magic bytes.</summary>
public static class ImageSniffer
{
    public sealed record ImageType(string ContentType, string Extension);

    public static ImageType? Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return new ImageType("image/jpeg", ".jpg");
        }

        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return new ImageType("image/png", ".png");
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return new ImageType("image/webp", ".webp");
        }

        return null;
    }
}
