using System.Net.Mail;
using Common.Domain;
using Common.Domain.ResultMonad;
using Identity.Domain.Common;

namespace Identity.Domain.Users;

public sealed class EmailAddress : ValueObject
{
    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static Result<EmailAddress> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return IdentityErrors.Required(
                nameof(EmailAddress),
                "Email address is required.");
        }

        var value = input.Trim().ToUpperInvariant();

        try
        {
            var parsed = new MailAddress(value);

            if (!string.Equals(
                    parsed.Address,
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                return IdentityErrors.Invalid(
                    nameof(EmailAddress),
                    "Email address is invalid.");
            }
        }
        catch (FormatException)
        {
            return IdentityErrors.Invalid(
                nameof(EmailAddress),
                "Email address is invalid.");
        }

        return new EmailAddress(value);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override int CompareTo(ValueObject? other)
    {
        return other is EmailAddress email
            ? string.Compare(
                Value,
                email.Value,
                StringComparison.Ordinal)
            : 1;
    }

    public override string ToString()
    {
        return Value;
    }
}
