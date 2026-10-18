using System.Globalization;
using System.Text.RegularExpressions;
using Commerce.Application.Abstractions;
using Commerce.Application.Boards;
using Commerce.Domain.Common;
using Commerce.Domain.Menus;
using Common.Application.Files;
using Common.Domain.ResultMonad;
using Microsoft.Extensions.Options;

namespace Commerce.Application.Images;

/// <summary>Kraft logos and menu photos on disk, one folder per tenant. File names come from ids only.</summary>
public interface ICommerceImageStorage
{
    /// <returns>The stored file name (no folders), e.g. <c>0f3c...-2.jpg</c>.</returns>
    Task<string> SaveAsync(Guid ownerId, int version, string extension, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    Stream? OpenRead(string fileName);

    void Delete(string fileName);
}

/// <summary>Public URLs for stored images (served by <c>GET /api/public/commerce-images/{file}</c>).</summary>
public static partial class CommerceImageUrls
{
    public const string Route = "/api/public/commerce-images/";

    public static string? For(string? fileName) => fileName is null ? null : Route + fileName;

    /// <summary>Only names this module writes: 32 hex, a dash, a version, an image extension.</summary>
    public static bool IsValidName(string fileName) => fileName.Length < 64 && StoredName().IsMatch(fileName);

    [GeneratedRegex("^[0-9a-f]{32}-[0-9]{1,6}\\.(jpg|png|webp)$", RegexOptions.None, 50)]
    private static partial Regex StoredName();
}

/// <summary>Uploading a Kraft's logo (Tenant Admin or its Owner) and a menu item's photo (the Owner).</summary>
public sealed class CommerceImageService(
    IKraftRepository krafts,
    IMenuItemRepository items,
    ICommerceImageStorage storage,
    ICommerceUnitOfWork unitOfWork,
    DisplaySignals displays,
    IOptions<CommerceOptions> options)
{
    public async Task<Result<string>> UploadLogoAsync(KraftId kraftId, Stream content, long length, CancellationToken cancellationToken)
    {
        var bytes = await ReadAsync(content, length, cancellationToken);
        if (bytes.IsFailure)
        {
            return bytes.Error!;
        }

        var kraft = await krafts.FindAsync(kraftId, cancellationToken);
        if (kraft is null)
        {
            return CommerceErrors.KraftNotFound;
        }

        var (data, format) = bytes.Value;
        var file = await storage.SaveAsync(kraftId.Value, kraft.LogoVersion + 1, format.Extension, data, cancellationToken);
        var replaced = kraft.ReplaceLogo(file);
        if (replaced.IsFailure)
        {
            storage.Delete(file);
            return replaced.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            storage.Delete(file);
            return saved.Error!;
        }

        DeleteOld(replaced.Value, file);
        return CommerceImageUrls.For(file)!;
    }

    public async Task<Result<string>> UploadMenuImageAsync(KraftId kraftId, MenuItemId itemId, Stream content, long length, CancellationToken cancellationToken)
    {
        var bytes = await ReadAsync(content, length, cancellationToken);
        if (bytes.IsFailure)
        {
            return bytes.Error!;
        }

        var item = await items.FindAsync(itemId, cancellationToken);
        if (item is null || item.KraftId != kraftId.Value)
        {
            return CommerceErrors.NotFound("MenuItemNotFound", "The menu item was not found.");
        }

        var (data, format) = bytes.Value;
        var file = await storage.SaveAsync(itemId.Value, item.ImageVersion + 1, format.Extension, data, cancellationToken);
        var replaced = item.ReplaceImage(file);
        if (replaced.IsFailure)
        {
            storage.Delete(file);
            return replaced.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            storage.Delete(file);
            return saved.Error!;
        }

        DeleteOld(replaced.Value, file);
        await displays.MenuChangedAsync(item.EventId, cancellationToken);
        return CommerceImageUrls.For(file)!;
    }

    public (Stream Content, string ContentType)? Open(string fileName)
    {
        if (!CommerceImageUrls.IsValidName(fileName))
        {
            return null;
        }

        return storage.OpenRead(fileName) is { } stream ? (stream, ImageFormat.ContentTypeOf(Path.GetExtension(fileName))) : null;
    }

    private void DeleteOld(string? previous, string current)
    {
        if (previous is not null && previous != current)
        {
            storage.Delete(previous);
        }
    }

    private async Task<Result<(byte[] Data, ImageFormat.Detected Format)>> ReadAsync(Stream content, long length, CancellationToken cancellationToken)
    {
        var max = options.Value.MaxImageBytes;
        var tooBig = CommerceErrors.Invalid("ImageInvalid", [$"File: Choose an image under {(max / (1024 * 1024)).ToString(CultureInfo.InvariantCulture)} MB."]);
        if (length <= 0 || length > max)
        {
            return tooBig;
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > max)
        {
            return tooBig;
        }

        var data = buffer.ToArray();
        return ImageFormat.Sniff(data) is { } format
            ? (data, format)
            : CommerceErrors.Invalid("ImageInvalid", ["File: Use a JPEG, PNG or WebP image."]);
    }
}
