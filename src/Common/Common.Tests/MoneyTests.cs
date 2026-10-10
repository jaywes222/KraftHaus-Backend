using System.Text.Json;
using Common.Domain.Currency;
using Xunit;

namespace Common.Tests;

public sealed class MoneyTests
{
    private static readonly Currency _otherCurrency = new("XXX");

    [Fact]
    public void OfAcceptsTwoDecimalsAndRejectsMore()
    {
        var valid = Money.Kes(100.25m);

        Assert.False(valid.IsFailure);

        var tooPrecise = Money.Kes(10.123m);

        Assert.True(tooPrecise.IsFailure);
        Assert.Equal(
            "money.too_precise",
            tooPrecise.Error!.Key);
    }

    [Fact]
    public void OfRejectsAmountsBeyondNumeric182()
    {
        var tooLarge =
            Money.Kes(10_000_000_000_000_000m);

        Assert.True(tooLarge.IsFailure);

        var max =
            Money.Kes(Money.MaxAmount);

        Assert.False(max.IsFailure);
    }

    [Fact]
    public void DefaultMoneyIsKesZero()
    {
        Money money = default;

        Assert.Equal(
            Currency.KES,
            money.Currency);

        Assert.True(money.IsZero);

        Assert.Equal(
            Money.Zero,
            money);
    }

    [Fact]
    public void EqualValuesAreEqualRegardlessOfTrailingZeros()
    {
        var ten =
            Money.Kes(10m).Value;

        var tenWithTrailingZeros =
            Money.Kes(10.00m).Value;

        Assert.Equal(
            ten,
            tenWithTrailingZeros);

        Assert.Equal(
            ten.GetHashCode(),
            tenWithTrailingZeros.GetHashCode());
    }

    [Fact]
    public void AddsAndSubtractsInSameCurrency()
    {
        var a =
            Money.Kes(10.50m).Value;

        var b =
            Money.Kes(0.25m).Value;

        Assert.Equal(
            Money.Kes(10.75m).Value,
            a + b);

        Assert.Equal(
            Money.Kes(10.25m).Value,
            a - b);

        Assert.Equal(
            Money.Kes(-10.50m).Value,
            -a);
    }

    [Fact]
    public void MixedCurrencyArithmeticAndComparisonThrow()
    {
        var kes =
            Money.KesWhole(10);

        var other =
            Money.ZeroOf(_otherCurrency);

        Assert.Throws<InvalidOperationException>(
            () => kes + other);

        Assert.Throws<InvalidOperationException>(
            () => kes - other);

        Assert.Throws<InvalidOperationException>(
            () => kes < other);
    }

    [Fact]
    public void PercentRoundsAsRequested()
    {
        var price =
            Money.KesWhole(1001);

        // 3.5% of 1001 = 35.035

        Assert.Equal(
            Money.Kes(35.04m).Value,
            price.Percent(3.5m));

        Assert.Equal(
            Money.Kes(35.03m).Value,
            price.Percent(
                3.5m,
                MoneyRounding.TowardZero));

        Assert.Equal(
            Money.Kes(35.04m).Value,
            price.Percent(
                3.5m,
                MoneyRounding.AwayFromZero));
    }

    [Fact]
    public void RoundToWholeUnitsSupportsTheThreePolicies()
    {
        var fee =
            Money.KesWhole(1001)
                .Percent(3.5m);

        // 35.04

        Assert.Equal(
            Money.KesWhole(35),
            fee.RoundToWholeUnits(
                MoneyRounding.Nearest));

        Assert.Equal(
            Money.KesWhole(36),
            fee.RoundToWholeUnits(
                MoneyRounding.AwayFromZero));

        Assert.Equal(
            Money.KesWhole(35),
            fee.RoundToWholeUnits(
                MoneyRounding.TowardZero));

        Assert.Equal(
            Money.KesWhole(-36),
            (-fee).RoundToWholeUnits(
                MoneyRounding.AwayFromZero));
    }

    [Fact]
    public void AllocateAlwaysSumsToTheOriginal()
    {
        var total =
            Money.Kes(1000.01m).Value;

        var parts =
            total.Allocate(1, 1, 1);

        Assert.Equal(
            Money.Kes(333.34m).Value,
            parts[0]);

        Assert.Equal(
            Money.Kes(333.34m).Value,
            parts[1]);

        Assert.Equal(
            Money.Kes(333.33m).Value,
            parts[2]);

        Assert.Equal(
            total,
            parts.Sum(Currency.KES));
    }

    [Fact]
    public void AllocateSupportsUnevenRatiosAndNegativeTotals()
    {
        var parts =
            Money.KesWhole(100)
                .Allocate(70, 30);

        Assert.Equal(
            Money.KesWhole(70),
            parts[0]);

        Assert.Equal(
            Money.KesWhole(30),
            parts[1]);

        var negative =
            (-Money.Kes(1000.01m).Value)
                .Allocate(1, 1, 1);

        Assert.Equal(
            -Money.Kes(1000.01m).Value,
            negative.Sum(Currency.KES));
    }

    [Fact]
    public void InstallmentsInWholeShillingsSplitFiftyFiftyWithTheOddShillingFirst()
    {
        var parts =
            Money.KesWhole(1501)
                .AllocateWholeUnits(1, 1);

        Assert.Equal(
            Money.KesWhole(751),
            parts[0]);

        Assert.Equal(
            Money.KesWhole(750),
            parts[1]);

        Assert.Equal(
            Money.KesWhole(1501),
            parts.Sum(Currency.KES));
    }

    [Fact]
    public void AllocateGuardsAgainstMisuse()
    {
        Assert.Throws<ArgumentException>(
            () => Money.KesWhole(10)
                .Allocate());

        Assert.Throws<ArgumentException>(
            () => Money.KesWhole(10)
                .Allocate(0, 0));

        Assert.Throws<ArgumentException>(
            () => Money.KesWhole(10)
                .Allocate(1, -1));

        Assert.Throws<InvalidOperationException>(
            () => Money.Kes(10.50m)
                .Value
                .AllocateWholeUnits(1, 1));
    }

    [Fact]
    public void ToWholeUnitsRequiresAWholeNumberOfShillings()
    {
        var whole =
            Money.Kes(1500.00m)
                .Value
                .ToWholeUnits();

        Assert.False(whole.IsFailure);

        Assert.Equal(
            1500L,
            whole.Value);

        var fractional =
            Money.Kes(1500.50m)
                .Value
                .ToWholeUnits();

        Assert.True(fractional.IsFailure);

        Assert.Equal(
            "money.not_whole_units",
            fractional.Error!.Key);
    }

    [Fact]
    public void ComparisonOrdersByAmount()
    {
        var five =
            Money.KesWhole(5);

        var six =
            Money.KesWhole(6);

        var seven =
            Money.KesWhole(7);

        Assert.True(five < six);
        Assert.True(six <= seven);
        Assert.True(seven > six);
        Assert.NotEqual(five, six);
    }

    [Fact]
    public void ToStringIsInvariantCulture()
    {
        Assert.Equal(
            "KES 1,500.00",
            Money.KesWhole(1500).ToString());
    }

    [Fact]
    public void JsonRoundTripsAndValidatesOnRead()
    {
        var money =
            Money.Kes(1500m).Value;

        var json =
            JsonSerializer.Serialize(money);

        Assert.Equal(
            "{\"amount\":1500.00,\"currency\":\"KES\"}",
            json);

        var deserialized =
            JsonSerializer.Deserialize<Money>(json);

        Assert.Equal(
            money,
            deserialized);

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<Money>(
                "{\"amount\":10.123,\"currency\":\"KES\"}"));

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<Money>(
                "{\"amount\":10,\"currency\":\"XXX\"}"));

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<Money>(
                "{\"amount\":10}"));
    }

    [Fact]
    public void SumOfEmptySequenceIsZeroInTheGivenCurrency()
    {
        var result =
            Array.Empty<Money>()
                .Sum(Currency.KES);

        Assert.Equal(
            Money.Zero,
            result);
    }
}
