using Common.Domain;
using Common.Domain.ResultMonad;

namespace Tenancy.Domain.Tenants;

public sealed class HostName : ValueObject
{
    private HostName(string value) => Value = value;

    public string Value { get; }

    public static Result<HostName> Create(string value)
    {
        var normalized = value.Trim()
            .TrimEnd('.')
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new Error
            {
                Key = "HostName.Invalid",
                Description = "Host name must not be empty."
            };
        }

        return Uri.CheckHostName(normalized) is not UriHostNameType.Dns
            ? (Result<HostName>)new Error
            {
                Key = "HostName.Invalid",
                Description = "Host name must be a valid DNS name."
            }
            : (Result<HostName>)new HostName(normalized);
    }

    public static HostName FromPersistence(string value) => new(value);

    protected override IEnumerable<object> GetEqualityComponents() =>
        [Value];

    public override int CompareTo(ValueObject? other) =>
        string.Compare(
            Value,
            (other as HostName)?.Value,
            StringComparison.Ordinal);

    public override string ToString() => Value;
}
