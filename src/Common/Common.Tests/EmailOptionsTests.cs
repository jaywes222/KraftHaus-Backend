
using Common.Application.Options;
using FluentAssertions;
using Xunit;

namespace Common.Tests;

public sealed class EmailOptionsTests
{
    private static EmailOptions CreateValid(
        EmailProvider provider = EmailProvider.Dummy)
    {
        return new EmailOptions
        {
            Provider = provider,
            BaseUrl = "https://api.example.com/",
            ApiKey = provider == EmailProvider.Dummy
                ? string.Empty
                : "test-api-key",
            FromAddress = "no-reply@example.com",
            FromName = "KraftHaus",
            Templates = new EmailTemplatesOptions
            {
                Otp =
                {
                    ["en"] = new EmailTemplateOptions
                    {
                        Subject =
                            "Your KraftHaus verification code",
                        TextBody =
                            "Your KraftHaus verification code is {0}.",
                        HtmlBody =
                            "<p>Your KraftHaus verification code is " +
                            "<strong>{0}</strong>.</p>"
                    }
                }
            }
        };
    }

    [Fact]
    public void ValidDummyConfigurationPasses()
    {
        var result =
            new EmailOptionsValidator()
                .Validate(CreateValid());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidBrevoConfigurationPasses()
    {
        var result =
            new EmailOptionsValidator()
                .Validate(
                    CreateValid(EmailProvider.Brevo));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void InvalidFromAddressFails()
    {
        var options = new EmailOptions
        {
            Provider = EmailProvider.Dummy,
            BaseUrl = "https://api.example.com/",
            ApiKey = string.Empty,
            FromAddress = "not-an-email",
            FromName = "KraftHaus",
            Templates = CreateValid().Templates
        };

        var result =
            new EmailOptionsValidator()
                .Validate(options);

        result.IsValid.Should().BeFalse();

        result.Errors
            .Should()
            .Contain(x =>
                x.PropertyName ==
                nameof(EmailOptions.FromAddress));
    }

    [Fact]
    public void MissingFromNameFails()
    {
        var options = new EmailOptions
        {
            Provider = EmailProvider.Dummy,
            BaseUrl = "https://api.example.com/",
            ApiKey = string.Empty,
            FromAddress = "no-reply@example.com",
            FromName = string.Empty,
            Templates = CreateValid().Templates
        };

        var result =
            new EmailOptionsValidator()
                .Validate(options);

        result.IsValid.Should().BeFalse();

        result.Errors
            .Should()
            .Contain(x =>
                x.PropertyName ==
                nameof(EmailOptions.FromName));
    }

    [Fact]
    public void NonDummyProviderRequiresApiKey()
    {
        var options = new EmailOptions
        {
            Provider = EmailProvider.Brevo,
            BaseUrl = "https://api.example.com/",
            ApiKey = string.Empty,
            FromAddress = "no-reply@example.com",
            FromName = "KraftHaus",
            Templates = CreateValid(EmailProvider.Brevo).Templates
        };

        var result =
            new EmailOptionsValidator()
                .Validate(options);

        result.IsValid.Should().BeFalse();

        result.Errors
            .Should()
            .Contain(x =>
                x.PropertyName ==
                nameof(EmailOptions.ApiKey));
    }
}
