using System.Text.RegularExpressions;

namespace Common.Domain.Phones;

/// <summary>
///     A Kenyan mobile number in E.164 (<c>+2547XXXXXXXX</c> or <c>+2541XXXXXXXX</c>), the form Spec 08 §9.5 requires
///     before sending and before any uniqueness check. Accepts the shapes people actually type: <c>07…</c>,
///     <c>01…</c>, <c>7…</c>, <c>1…</c>, <c>254…</c>, <c>+254…</c>, with spaces, dashes or brackets.
/// </summary>
public sealed partial record KenyanMobile
{
    private KenyanMobile(string value) => Value = value;

    public string Value { get; }

    public static bool TryCreate(string? input, out KenyanMobile? mobile)
    {
        mobile = null;

        if (string.IsNullOrWhiteSpace(input) || input.Length > 32)
        {
            return false;
        }

        var digits = Separators().Replace(input.Trim(), string.Empty);

        var national = digits switch
        {
            _ when digits.StartsWith("+254", StringComparison.Ordinal) => digits[4..],
            _ when digits.StartsWith("254", StringComparison.Ordinal) && digits.Length == 12 => digits[3..],
            _ when digits.StartsWith('0') && digits.Length == 10 => digits[1..],
            _ when digits.Length == 9 => digits,
            _ => null
        };

        if (national is null || !National().IsMatch(national))
        {
            return false;
        }

        mobile = new KenyanMobile($"+254{national}");
        return true;
    }

    /// <summary>Rehydrates a stored number (already normalised when it was written).</summary>
    public static KenyanMobile FromStored(string value) => new(value);

    public override string ToString() => Value;

    [GeneratedRegex(@"[\s\-().]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Separators();

    [GeneratedRegex(@"^[17]\d{8}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex National();
}
