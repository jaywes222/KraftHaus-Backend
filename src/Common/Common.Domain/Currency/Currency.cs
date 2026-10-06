using System.Text.Json;
using System.Text.Json.Serialization;
using Common.Domain.ResultMonad;

namespace Common.Domain.Currency;

/// <summary>
/// ISO 4217 currency. Only KES is supported for Event 001. To support another currency,
/// add it to <see cref="_registry"/>; everything else follows.
/// <c>default(Currency)</c> is KES, so default-constructed values (EF, serialisers) are valid.
/// </summary>
[JsonConverter(typeof(CurrencyJsonConverter))]
public readonly struct Currency : IEquatable<Currency>
{
    private static readonly Dictionary<string, int> _registry = new(StringComparer.Ordinal)
    {
        ["KES"] = 2
    };

    private readonly string? _code;

    // internal: tests can create "other" currencies to prove mixed-currency arithmetic is rejected
    internal Currency(string code) => _code = code;

    public static Currency KES { get; } = new("KES");

    /// <summary>ISO 4217 code, upper case.</summary>
    public string Code => _code ?? "KES";

    /// <summary>Number of decimal places of the minor unit (KES: 2).</summary>
    public int MinorUnitDigits => _registry.TryGetValue(Code, out var digits) ? digits : 2;

    public static Result<Currency> From(string? code)
    {
        var normalised = code?.Trim().ToUpperInvariant();
        return normalised is not null && _registry.ContainsKey(normalised)
            ? new Currency(normalised)
            : MoneyErrors.UnsupportedCurrency(code);
    }

    public bool Equals(Currency other) => string.Equals(Code, other.Code, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is Currency other && Equals(other);
    public override int GetHashCode() => Code.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => Code;

    public static bool operator ==(Currency left, Currency right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Currency left, Currency right)
    {
        return !left.Equals(right);
    }
}

internal sealed class CurrencyJsonConverter : JsonConverter<Currency>
{
    public override Currency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = Currency.From(reader.GetString());
        return result.IsFailure
            ? throw new JsonException(result.Error!.ToString())
            : result.Value;
    }

    public override void Write(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Code);
}
