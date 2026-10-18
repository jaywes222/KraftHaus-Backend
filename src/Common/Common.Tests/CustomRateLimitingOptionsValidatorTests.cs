using Common.Application.Options;
using Xunit;

namespace Common.Tests;

public sealed class CustomRateLimitingOptionsValidatorTests
{
    private readonly CustomRateLimitingOptionsValidator _validator = new();

    [Fact]
    public void ValidateWhenAllLimitsAreValidShouldSucceed()
    {
        var options = CreateValidOptions();

        var result = _validator.Validate(options);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateWhenLimitIsNotPositiveShouldFail(int limit)
    {
        var options = CreateValidOptions();
        options.Global.Limit = limit;

        var result = _validator.Validate(options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.PropertyName.Contains(
                "Global.Limit",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ValidateWhenPeriodIsInvalidShouldFail(double period)
    {
        var options = CreateValidOptions();
        options.Global.PeriodInMs = period;

        var result = _validator.Validate(options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.PropertyName.Contains(
                "Global.PeriodInMs",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    public void ValidateWhenQueueLimitIsNegativeShouldFail(int queueLimit)
    {
        var options = CreateValidOptions();
        options.Global.QueueLimit = queueLimit;

        var result = _validator.Validate(options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.PropertyName.Contains(
                "Global.QueueLimit",
                StringComparison.Ordinal));
    }

    private static CustomRateLimitingOptions CreateValidOptions()
    {
        static FixedWindow Window() => new()
        {
            Limit = 10,
            PeriodInMs = 60_000,
            QueueLimit = 0
        };

        return new CustomRateLimitingOptions
        {
            Global = Window(),
            Sms = Window(),
            Register = Window(),
            CreateStore = Window(),
            TokenCreate = Window(),
            CheckRegistration = Window(),
            TokenRefresh = Window(),
            OtpVerify = Window(),
            Waitlist = Window(),
            Checkout = Window()
        };
    }
}
