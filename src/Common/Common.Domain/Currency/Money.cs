using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Common.Domain.ResultMonad;

namespace Common.Domain.Currency;

/// <summary>
/// How to round when a calculation produces more digits than the target unit.
/// </summary>
public enum MoneyRounding
{
    /// <summary>
    /// Nearest, with .5 rounding away from zero (what a customer expects).
    /// </summary>
    Nearest = 0,

    /// <summary>
    /// Always increase the magnitude
    /// (3.01 -> 4 when rounding to whole units).
    /// </summary>
    AwayFromZero = 1,

    /// <summary>
    /// Always decrease the magnitude
    /// (3.99 -> 3 when rounding to whole units).
    /// </summary>
    TowardZero = 2
}

/// <summary>
/// An amount of money in one currency. Backed by <see cref="decimal"/>; no floating point anywhere.
/// Fits a database <c>numeric(18,2)</c>. Immutable. <c>default(Money)</c> is KES 0.00.
///
/// Mixed-currency arithmetic is a programming error and throws; invalid input from users or
/// other systems goes through <see cref="Of"/>, which returns a <see cref="Result{T}"/>.
/// </summary>
[JsonConverter(typeof(MoneyJsonConverter))]
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    /// <summary>
    /// Largest magnitude that fits numeric(18,2).
    /// </summary>
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public static Money Zero => default;

    public static Money ZeroOf(Currency currency)
        => new(Scale(0m, currency.MinorUnitDigits), currency);

    public bool IsZero => Amount == 0m;

    public bool IsPositive => Amount > 0m;

    public bool IsNegative => Amount < 0m;

    /// <summary>
    /// True when the amount has no fractional part
    /// (a whole number of shillings for KES).
    /// </summary>
    public bool IsWholeUnits
        => Amount == decimal.Truncate(Amount);

    // -------------------------------------------------------------------------
    // Creation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates an amount from outside the domain. Never rounds silently.
    /// </summary>
    public static Result<Money> Of(decimal amount, Currency currency)
    {
        var digits = currency.MinorUnitDigits;

        if (decimal.Round(amount, digits) != amount)
        {
            return MoneyErrors.TooPrecise(currency);
        }

        if (Math.Abs(amount) > MaxAmount)
        {
            return MoneyErrors.OutOfRange();
        }

        return new Money(
            Scale(amount, digits),
            currency);
    }

    public static Result<Money> Kes(decimal amount)
        => Of(amount, Currency.KES);

    /// <summary>
    /// Whole shillings, for prices and constants in code.
    /// </summary>
    public static Money KesWhole(long shillings)
        => new(
            Scale(shillings, Currency.KES.MinorUnitDigits),
            Currency.KES);

    // -------------------------------------------------------------------------
    // Arithmetic
    // -------------------------------------------------------------------------

    public static Money operator +(Money a, Money b)
    {
        RequireSameCurrency(a, b);

        var amount = a.Amount + b.Amount;

        EnsureWithinRange(amount);

        return new Money(
            Scale(amount, a.Currency.MinorUnitDigits),
            a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        RequireSameCurrency(a, b);

        var amount = a.Amount - b.Amount;

        EnsureWithinRange(amount);

        return new Money(
            Scale(amount, a.Currency.MinorUnitDigits),
            a.Currency);
    }

    public static Money operator -(Money a)
    {
        EnsureWithinRange(-a.Amount);

        return new Money(
            Scale(-a.Amount, a.Currency.MinorUnitDigits),
            a.Currency);
    }

    /// <summary>
    /// Multiplies and rounds to the currency's minor unit
    /// (nearest, .5 away from zero).
    /// </summary>
    public static Money operator *(Money a, decimal factor)
    {
        return a.Multiply(factor);
    }

    public static Money operator *(Money a, int factor)
    {
        var amount = a.Amount * factor;

        EnsureWithinRange(amount);

        return new Money(
            Scale(amount, a.Currency.MinorUnitDigits),
            a.Currency);
    }

    public Money Multiply(
        decimal factor,
        MoneyRounding rounding = MoneyRounding.Nearest)
    {
        var result = RoundValue(
            Amount * factor,
            Currency.MinorUnitDigits,
            rounding);

        EnsureWithinRange(result);

        return new Money(
            Scale(result, Currency.MinorUnitDigits),
            Currency);
    }

    /// <summary>
    /// A percentage of this amount.
    ///
    /// Example:
    /// <c>price.Percent(3.5m)</c> for a 3.5% fee.
    /// </summary>
    public Money Percent(
        decimal percent,
        MoneyRounding rounding = MoneyRounding.Nearest)
    {
        return Multiply(percent / 100m, rounding);
    }

    /// <summary>
    /// Rounds to whole units (whole shillings for KES).
    /// M-Pesa STK amounts must be whole.
    /// </summary>
    public Money RoundToWholeUnits(MoneyRounding rounding)
    {
        var result = RoundValue(
            Amount,
            0,
            rounding);

        EnsureWithinRange(result);

        return new Money(
            Scale(result, Currency.MinorUnitDigits),
            Currency);
    }

    /// <summary>
    /// Splits this amount by ratios so that the parts add up to exactly
    /// the original.
    ///
    /// Leftover minor units (cents) go one at a time to the earliest parts.
    ///
    /// Example:
    /// 1000.01 split [1,1,1] ->
    /// 333.34, 333.33, 333.34
    /// </summary>
    public Money[] Allocate(params int[] ratios)
        => AllocateCore(
            ratios,
            Currency.MinorUnitDigits);

    /// <summary>
    /// Like <see cref="Allocate"/> but in whole units.
    /// The amount must already be whole.
    ///
    /// Example:
    /// 1501 split [1,1] ->
    /// 751, 750
    /// </summary>
    public Money[] AllocateWholeUnits(params int[] ratios)
    {
        if (!IsWholeUnits)
        {
            throw new InvalidOperationException(
                $"Cannot allocate {this} in whole units: " +
                "the amount has a fractional part.");
        }

        return AllocateCore(ratios, 0);
    }

    // -------------------------------------------------------------------------
    // Conversion
    // -------------------------------------------------------------------------

    /// <summary>
    /// The amount as a whole number of units (shillings).
    /// Fails if there is a fractional part.
    /// </summary>
    public Result<long> ToWholeUnits()
        => IsWholeUnits
            ? (long)Amount
            : MoneyErrors.NotWhole(this);

    // -------------------------------------------------------------------------
    // Comparison and formatting
    // -------------------------------------------------------------------------

    public int CompareTo(Money other)
    {
        RequireSameCurrency(this, other);

        return Amount.CompareTo(other.Amount);
    }

    public static bool operator <(Money a, Money b)
        => a.CompareTo(b) < 0;

    public static bool operator >(Money a, Money b)
        => a.CompareTo(b) > 0;

    public static bool operator <=(Money a, Money b)
        => a.CompareTo(b) <= 0;

    public static bool operator >=(Money a, Money b)
        => a.CompareTo(b) >= 0;

    public bool Equals(Money other)
        => Amount == other.Amount
           && Currency.Equals(other.Currency);

    public override bool Equals(object? obj)
        => obj is Money other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(
            Amount,
            Currency);

    public static bool operator ==(Money a, Money b)
        => a.Equals(b);

    public static bool operator !=(Money a, Money b)
        => !a.Equals(b);

    /// <summary>
    /// Invariant-culture display.
    ///
    /// Example:
    /// "KES 1,500.00"
    ///
    /// Not intended for storage or APIs.
    /// </summary>
    public override string ToString()
        => $"{Currency} {Amount.ToString(
            "N" + Currency.MinorUnitDigits,
            CultureInfo.InvariantCulture)}";

    // -------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------

    private static void RequireSameCurrency(
        Money a,
        Money b)
    {
        if (a.Currency != b.Currency)
        {
            throw new InvalidOperationException(
                $"Currency mismatch: {a.Currency} and {b.Currency}.");
        }
    }

    private static void EnsureWithinRange(decimal amount)
    {
        if (Math.Abs(amount) > MaxAmount)
        {
            throw new OverflowException(
                $"Money amount '{amount.ToString(
                    CultureInfo.InvariantCulture)}' " +
                $"exceeds the supported maximum magnitude of " +
                $"{MaxAmount.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    /// <summary>
    /// Keeps a fixed number of decimals so 10 and 10.00
    /// serialize the same way.
    /// </summary>
    private static decimal Scale(
        decimal value,
        int digits)
        => decimal.Round(value, digits)
           + new decimal(
               0,
               0,
               0,
               false,
               (byte)digits);

    private static decimal Pow10(int digits)
    {
        var result = 1m;

        for (var i = 0; i < digits; i++)
        {
            result *= 10m;
        }

        return result;
    }

    private static decimal RoundValue(
        decimal value,
        int digits,
        MoneyRounding mode)
    {
        return mode switch
        {
            MoneyRounding.Nearest =>
                Math.Round(
                    value,
                    digits,
                    MidpointRounding.AwayFromZero),

            MoneyRounding.AwayFromZero =>
                Math.Round(
                    value,
                    digits,
                    value >= 0
                        ? MidpointRounding.ToPositiveInfinity
                        : MidpointRounding.ToNegativeInfinity),

            MoneyRounding.TowardZero =>
                Math.Round(
                    value,
                    digits,
                    MidpointRounding.ToZero),

            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                null)
        };
    }

    private Money[] AllocateCore(
        int[] ratios,
        int digits)
    {
        if (ratios is null || ratios.Length == 0)
        {
            throw new ArgumentException(
                "At least one ratio is required.",
                nameof(ratios));
        }

        if (ratios.Any(r => r < 0))
        {
            throw new ArgumentException(
                "Ratios cannot be negative.",
                nameof(ratios));
        }

        var ratioSum = ratios.Sum(r => (long)r);

        if (ratioSum == 0)
        {
            throw new ArgumentException(
                "At least one ratio must be positive.",
                nameof(ratios));
        }

        // Work in integral units:
        // cents for currencies with 2 minor digits,
        // whole units for AllocateWholeUnits.
        //
        // Work on the magnitude and restore the sign at the end.
        var unitsPerWhole = Pow10(digits);

        var totalUnits =
            decimal.Truncate(
                Math.Abs(Amount) * unitsPerWhole);

        var sign = Amount < 0 ? -1 : 1;

        var shares = new decimal[ratios.Length];

        decimal allocated = 0m;

        for (var i = 0; i < ratios.Length; i++)
        {
            shares[i] =
                decimal.Floor(
                    totalUnits * ratios[i] / ratioSum);

            allocated += shares[i];
        }

        var remainder = totalUnits - allocated;

        // Distribute leftover units one at a time to the
        // earliest positive ratios.
        var index = 0;

        while (remainder > 0)
        {
            if (ratios[index] > 0)
            {
                shares[index]++;
                remainder--;
            }

            index = (index + 1) % ratios.Length;
        }

        var currency = Currency;
        var minorDigits = currency.MinorUnitDigits;

        return shares
            .Select(
                share => new Money(
                    Scale(
                        sign * share / unitsPerWhole,
                        minorDigits),
                    currency))
            .ToArray();
    }

    // -------------------------------------------------------------------------
    // Static arithmetic helpers
    // -------------------------------------------------------------------------

    public static Money Add(
        Money left,
        Money right)
    {
        return left + right;
    }

    public static Money Subtract(
        Money left,
        Money right)
    {
        return left - right;
    }

    public static Money Negate(
        Money item)
    {
        return -item;
    }
}

// =============================================================================
// Money Extensions
// =============================================================================

public static class MoneyExtensions
{
    /// <summary>
    /// Sums money values.
    ///
    /// An empty sequence gives zero in <paramref name="currency"/>.
    /// </summary>
    public static Money Sum(
        this IEnumerable<Money> source,
        Currency currency)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Aggregate(
            Money.ZeroOf(currency),
            (total, next) => total + next);
    }
}

// =============================================================================
// Money Errors
// =============================================================================

/// <summary>
/// Stable error codes for money validation.
/// </summary>
public static class MoneyErrors
{
    public static Error TooPrecise(
        Currency currency)
        => Make(
            "money.too_precise",
            $"Amount has more than " +
            $"{currency.MinorUnitDigits} decimal places for {currency}.");

    public static Error OutOfRange()
        => Make(
            "money.out_of_range",
            $"Amount must not exceed " +
            $"{Money.MaxAmount.ToString(CultureInfo.InvariantCulture)}.");

    public static Error NotWhole(
        Money money)
        => Make(
            "money.not_whole_units",
            $"{money} is not a whole number of units.");

    public static Error UnsupportedCurrency(
        string? code)
        => Make(
            "money.unsupported_currency",
            $"Currency '{code}' is not supported.");

    private static Error Make(
        string code,
        string message)
    {
        return new Error
        {
            Key = code,
            Description = message
        };
    }
}

// =============================================================================
// JSON Converter
// =============================================================================

internal sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(
                "Money must be a JSON object with " +
                "'amount' and 'currency'.");
        }

        decimal? amount = null;
        Currency? currency = null;

        while (
            reader.Read() &&
            reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException();
            }

            var name = reader.GetString();

            if (!reader.Read())
            {
                throw new JsonException(
                    "Unexpected end of Money JSON.");
            }

            if (string.Equals(
                    name,
                    "amount",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (reader.TokenType != JsonTokenType.Number)
                {
                    throw new JsonException(
                        "'amount' must be a JSON number.");
                }

                amount = reader.GetDecimal();
            }
            else if (string.Equals(
                         name,
                         "currency",
                         StringComparison.OrdinalIgnoreCase))
            {
                if (reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException(
                        "'currency' must be a JSON string.");
                }

                var currencyCode = reader.GetString();

                var parsed = Currency.From(currencyCode);

                currency = parsed.IsFailure
                    ? throw new JsonException(
                        parsed.Error!.ToString())
                    : parsed.Value;
            }
            else
            {
                reader.Skip();
            }
        }

        if (amount is null || currency is null)
        {
            throw new JsonException(
                "Money requires both 'amount' and 'currency'.");
        }

        var money = Money.Of(
            amount.Value,
            currency.Value);

        return money.IsFailure
            ? throw new JsonException(
                money.Error!.ToString())
            : money.Value;
    }

    public override void Write(
        Utf8JsonWriter writer,
        Money value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteNumber(
            "amount",
            value.Amount);

        writer.WriteString(
            "currency",
            value.Currency.Code);

        writer.WriteEndObject();
    }
}
