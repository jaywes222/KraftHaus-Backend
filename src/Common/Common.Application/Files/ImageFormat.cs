namespace Common.Application.Files;

/// <summary>JPEG, PNG or WebP recognised by their first bytes (never by the uploaded name or content type).</summary>
public static class ImageFormat
{
    public sealed record Detected(string Extension, string ContentType);

    public static Detected? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes is [0xFF, 0xD8, 0xFF, ..])
        {
            return new Detected(".jpg", "image/jpeg");
        }

        if (bytes is [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..])
        {
            return new Detected(".png", "image/png");
        }

        if (bytes.Length > 12 && bytes is [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..])
        {
            return new Detected(".webp", "image/webp");
        }

        return null;
    }

    public static string ContentTypeOf(string extension) =>
        extension.ToUpperInvariant() switch
        {
            ".PNG" => "image/png",
            ".WEBP" => "image/webp",
            _ => "image/jpeg"
        };
}
