using System.Security.Cryptography;

namespace Common.Domain.Codes;

/// <summary>
///     Random codes people read aloud or type (order references, manual check-in codes): Crockford base32 without
///     I, L, O, U, so 0/O and 1/I/L are never confused.
/// </summary>
public static class ShortCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string New(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 4);

        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>Upper-cases and strips spaces and dashes; maps the confusable letters a person may type.</summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return new string([
            .. input.Trim().ToUpperInvariant()
                .Where(c => c is not (' ' or '-'))
                .Select(c => c switch
                {
                    'O' => '0',
                    'I' or 'L' => '1',
                    _ => c
                })
        ]);
    }
}
