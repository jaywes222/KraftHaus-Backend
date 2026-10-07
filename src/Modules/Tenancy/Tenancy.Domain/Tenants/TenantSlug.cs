using System.Text.RegularExpressions;
using Common.Domain;
using Common.Domain.ResultMonad;

namespace Tenancy.Domain.Tenants;

public sealed class TenantSlug : ValueObject
{
    private static readonly Regex _pattern = new(
        "^[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private TenantSlug(string value) => Value = value;

    public string Value { get; }

    public static Result<TenantSlug> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Error
            {
                Key = "TenantSlug.Required",
                Description = "Tenant slug is required."
            };
        }

        var normalized = value
            .Trim()
            .ToUpperInvariant();

        if (normalized.Length is < 1 or > 63 ||
            !_pattern.IsMatch(normalized))
        {
            return new Error
            {
                Key = "TenantSlug.Invalid",
                Description =
                    "Tenant slug must be 1-63 characters and contain only letters, digits and hyphens."
            };
        }

        return new TenantSlug(normalized);
    }

    public static TenantSlug FromPersistence(string value) =>
        new(value);

    protected override IEnumerable<object> GetEqualityComponents() =>
        [Value];

    public override int CompareTo(ValueObject? other) =>
        string.Compare(
            Value,
            (other as TenantSlug)?.Value,
            StringComparison.Ordinal);

    public override string ToString() => Value;
}
