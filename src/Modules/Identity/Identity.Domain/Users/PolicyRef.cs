using Common.Domain;
using Common.Domain.ResultMonad;
using Identity.Domain.Common;

namespace Identity.Domain.Users;

public sealed class PolicyRef : ValueObject
{
    private PolicyRef(
        string key,
        int version,
        string textHash)
    {
        Key = key;
        Version = version;
        TextHash = textHash;
    }

    public string Key { get; }

    public int Version { get; }

    public string TextHash { get; }

    public static Result<PolicyRef> Create(
        string? key,
        int version,
        string? textHash)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return IdentityErrors.Required(
                nameof(Key),
                "Policy key is required.");
        }

        if (version <= 0)
        {
            return IdentityErrors.Invalid(
                nameof(Version),
                "Policy version must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(textHash))
        {
            return IdentityErrors.Required(
                nameof(TextHash),
                "Policy text hash is required.");
        }

        return new PolicyRef(
            key.Trim(),
            version,
            textHash.Trim());
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Key;
        yield return Version;
        yield return TextHash;
    }

    public override int CompareTo(ValueObject? other)
    {
        if (other is not PolicyRef policy)
        {
            return 1;
        }

        var key = string.Compare(
            Key,
            policy.Key,
            StringComparison.Ordinal);

        if (key != 0)
        {
            return key;
        }

        var version = Version.CompareTo(policy.Version);

        return version != 0
            ? version
            : string.Compare(
                TextHash,
                policy.TextHash,
                StringComparison.Ordinal);
    }
}
