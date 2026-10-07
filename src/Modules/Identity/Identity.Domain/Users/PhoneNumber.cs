using Common.Domain;
using Common.Domain.ResultMonad;
using Identity.Domain.Common;

namespace Identity.Domain.Users;

public sealed class PhoneNumber : ValueObject
{
    private PhoneNumber(string value) => Value = value;

    public string Value { get; }

    public static Result<PhoneNumber> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return IdentityErrors.Required(
                nameof(PhoneNumber),
                "Phone number is required.");
        }

        var value = input
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal);

        if (value.StartsWith("00", StringComparison.Ordinal))
        {
            value = "+" + value[2..];
        }
        else if (value.StartsWith('0') && value.Length == 10)
        {
            value = "+254" + value[1..];
        }
        else if (value.Length == 9 &&
                 (value[0] == '7' || value[0] == '1'))
        {
            value = "+254" + value;
        }
        else if (value.StartsWith("254", StringComparison.Ordinal))
        {
            value = "+" + value;
        }
        else if (!value.StartsWith('+'))
        {
            return IdentityErrors.Invalid(
                nameof(PhoneNumber),
                "Phone number must be in a supported international format.");
        }

        var digits = value[1..];

        if (digits.Length is < 7 or > 15 ||
            !digits.All(char.IsDigit))
        {
            return IdentityErrors.Invalid(
                nameof(PhoneNumber),
                "Phone number must contain 7 to 15 digits.");
        }

        if (value.StartsWith("+254", StringComparison.Ordinal))
        {
            var local = digits[3..];

            if (local.Length != 9 ||
                (local[0] != '7' && local[0] != '1'))
            {
                return IdentityErrors.Invalid(
                    nameof(PhoneNumber),
                    "The Kenyan number must be a mobile number.");
            }
        }

        return new PhoneNumber(value);
    }

    public string Masked()
    {
        return Value.Length <= 6 ? "***" : $"{Value[..4]}***{Value[^3..]}";
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override int CompareTo(ValueObject? other)
    {
        return other is PhoneNumber phone
            ? string.Compare(
                Value,
                phone.Value,
                StringComparison.Ordinal)
            : 1;
    }

    public override string ToString()
    {
        return Value;
    }
}
