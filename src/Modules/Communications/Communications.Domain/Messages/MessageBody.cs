using System.Text;
using System.Text.RegularExpressions;

namespace Communications.Domain.Messages;

/// <summary>
///     What the message log keeps of a body. Anything that looks like a code (a word of 4+ letters and digits with at
///     least one digit: OTPs, ticket codes, order references) is masked, so the log never holds a usable secret.
/// </summary>
public static partial class MessageBody
{
    public const int StoredMaxLength = 1000;

    public static string Mask(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }

        var masked = CodeLike().Replace(body, match => new string('•', match.Length));

        return masked.Length <= StoredMaxLength ? masked : masked[..StoredMaxLength];
    }

    /// <summary>SMS parts the body takes: 160 characters for one, 153 each when split (GSM-7; Unicode takes more).</summary>
    public static int SmsParts(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var unicode = body.Any(c => c > 0x7F);
        var single = unicode ? 70 : 160;
        var part = unicode ? 67 : 153;
        var length = unicode ? body.Length : Encoding.ASCII.GetByteCount(body);

        return length <= single ? 1 : (length + part - 1) / part;
    }

    [GeneratedRegex(@"\b(?=[A-Za-z0-9]*\d)[A-Za-z0-9]{4,}\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex CodeLike();
}
