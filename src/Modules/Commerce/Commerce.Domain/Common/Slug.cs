using System.Globalization;
using System.Text;

namespace Commerce.Domain.Common;

/// <summary>URL slugs: lower-case ASCII letters and digits separated by single dashes.</summary>
public static class Slug
{
    public const int MaxLength = 60;

    public static string From(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length);
        var dash = false;

        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                dash = false;
            }
            else if (!dash && builder.Length > 0)
            {
                builder.Append('-');
                dash = true;
            }
        }

        var slug = builder.ToString().Trim('-');

        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].Trim('-');
        }

        return slug.Length == 0 ? "kraft" : slug;
    }
}
