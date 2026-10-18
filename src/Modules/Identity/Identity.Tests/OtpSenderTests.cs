using Common.Application.Notifications;
using Common.Application.Options;
using FluentAssertions;
using Identity.Application.Otp;
using Identity.Domain.Otp;
using Microsoft.Extensions.Options;
using Xunit;

namespace Identity.Tests;

public sealed class OtpSenderTests
{
    [Fact]
    public async Task PhoneOtpIsSentThroughSmsGateway()
    {
        var sms =
            new RecordingSmsGateway();

        var email =
            new RecordingEmailGateway();

        var sender =
            new OtpSender(
                sms,
                email,
                SmsTemplate("Your KRAFTHAUS code is {0}. Don't share it."));

        await sender.SendAsync(
            OtpChannel.Phone,
            "+254700000000",
            "123456",
            OtpPurpose.SignIn,
            CancellationToken.None);

        sms.Messages
            .Should()
            .ContainSingle();

        var message =
            sms.Messages.Single();

        message.PhoneNumber
            .Should()
            .Be("+254700000000");

        // The configured Notifications:Sms:Templates:Otp wording, never a bare code (reads as spam).
        message.Message
            .Should()
            .Be("Your KRAFTHAUS code is 123456. Don't share it.");

        email.Messages
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task EmailOtpIsSentThroughEmailGateway()
    {
        var sms =
            new RecordingSmsGateway();

        var email =
            new RecordingEmailGateway();

        var sender =
            new OtpSender(
                sms,
                email,
                SmsTemplate("Your KRAFTHAUS code is {0}. Don't share it."));

        await sender.SendAsync(
            OtpChannel.Email,
            "user@example.com",
            "654321",
            OtpPurpose.SignIn,
            CancellationToken.None);

        email.Messages
            .Should()
            .ContainSingle();

        var message =
            email.Messages.Single();

        message.To
            .Should()
            .Be("user@example.com");

        message.Subject
            .Should()
            .Be(
                "Your KraftHaus verification code");

        message.TextBody
            .Should()
            .Contain("654321");

        message.HtmlBody
            .Should()
            .Contain("654321");

        sms.Messages
            .Should()
            .BeEmpty();
    }

    private static IOptions<SmsOptions> SmsTemplate(string template)
    {
        var options = new SmsOptions
        {
            Provider = SmsProvider.Dummy,
            BaseUrl = "https://api.example.com/",
            ApiKey = string.Empty,
            SenderId = string.Empty,
            ThrottleCounterTtlHours = 48,
            Templates = new SmsTemplatesOptions()
        };
        options.Templates.Otp["en"] = template;
        return Options.Create(options);
    }
    private sealed class RecordingSmsGateway
        : ISmsGateway
    {
        public List<SmsMessage> Messages { get; } = [];

        public Task SendAsync(
            SmsMessage message,
            CancellationToken cancellationToken)
        {
            Messages.Add(message);

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEmailGateway
        : IEmailGateway
    {
        public List<EmailMessage> Messages { get; } = [];

        public Task SendAsync(
            EmailMessage message,
            CancellationToken cancellationToken)
        {
            Messages.Add(message);

            return Task.CompletedTask;
        }
    }
}
