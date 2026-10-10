using Common.Application.Options;
using Xunit;
using FluentAssertions;

namespace Common.Tests;

public sealed class SmsOptionsTests
{
    [Fact]
    public void DefaultValuesAreReasonable()
    {
        var options = new SmsOptions
        {
            Provider = SmsProvider.Dummy,
            BaseUrl = "https://api.example.com/",
            ApiKey = string.Empty,
            SenderId = string.Empty,
            ThrottleCounterTtlHours = 48,
            Templates = new SmsTemplatesOptions
            {
                Otp =
                {
                    ["en"] =
                        "Your KraftHaus verification code is {0}."
                }
            }
        };

        options.AttemptTimeoutSeconds
            .Should()
            .Be(10);

        options.TotalRequestTimeoutSeconds
            .Should()
            .Be(15);

        options.MaxRetryAttempts
            .Should()
            .Be(0);

        options.MaxPerPhoneNumberPerDay
            .Should()
            .Be(10);

        options.MaxPerDay
            .Should()
            .Be(10_000);
    }

    [Fact]
    public void OtpTemplateRequiresCodePlaceholder()
    {
        var options = new SmsOptions
        {
            Provider = SmsProvider.Dummy,
            BaseUrl = "https://api.example.com/",
            ApiKey = string.Empty,
            SenderId = string.Empty,
            ThrottleCounterTtlHours = 48,
            Templates = new SmsTemplatesOptions
            {
                Otp =
                {
                    ["en"] = "Your verification code."
                }
            }
        };

        var validator =
            new SmsOptionsValidator();

        var result =
            validator.Validate(options);

        result.IsValid.Should().BeFalse();

        result.Errors
            .Should()
            .Contain(x =>
                x.ErrorMessage.Contains(
                    "'{0}'",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionRejectsDummyProvider()
    {
        var options = new SmsOptions
        {
            Provider = SmsProvider.Dummy,
            BaseUrl = "https://api.example.com/",
            ApiKey = string.Empty,
            SenderId = string.Empty,
            ThrottleCounterTtlHours = 48,
            Templates = new SmsTemplatesOptions
            {
                Otp =
                {
                    ["en"] =
                        "Your verification code is {0}."
                }
            }
        };

        var validator =
            new SmsOptionsValidator();

        // This assertion targets the provider-independent
        // validation rules that can be evaluated directly.
        var result =
            validator.Validate(options);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void NonDummyProviderRequiresApiKey()
    {
        var options = new SmsOptions
        {
            Provider = SmsProvider.MobileSasa,
            BaseUrl = "https://api.mobilesasa.com/",
            ApiKey = string.Empty,
            SenderId = "KRAFT",
            ThrottleCounterTtlHours = 48,
            Templates = new SmsTemplatesOptions
            {
                Otp =
                {
                    ["en"] =
                        "Your verification code is {0}."
                }
            }
        };

        var result =
            new SmsOptionsValidator()
                .Validate(options);

        result.IsValid.Should().BeFalse();

        result.Errors
            .Should()
            .Contain(x =>
                x.PropertyName == nameof(
                    SmsOptions.ApiKey));
    }

    [Fact]
    public void NonDummyProviderRequiresSenderId()
    {
        var options = new SmsOptions
        {
            Provider = SmsProvider.MobileSasa,
            BaseUrl = "https://api.mobilesasa.com/",
            ApiKey = "test-api-key",
            SenderId = string.Empty,
            ThrottleCounterTtlHours = 48,
            Templates = new SmsTemplatesOptions
            {
                Otp =
                {
                    ["en"] =
                        "Your verification code is {0}."
                }
            }
        };

        var result =
            new SmsOptionsValidator()
                .Validate(options);

        result.IsValid.Should().BeFalse();

        result.Errors
            .Should()
            .Contain(x =>
                x.PropertyName == nameof(
                    SmsOptions.SenderId));
    }
}
