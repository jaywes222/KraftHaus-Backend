using System.Globalization;
using Common.Application.Notifications;
using Common.Application.Options;
using Identity.Application.Abstractions;
using Identity.Domain.Otp;
using Microsoft.Extensions.Options;

namespace Identity.Application.Otp;

/// <summary>
///     Sends a one-time code. An SMS uses the <c>Notifications:Sms:Templates:Otp</c> wording ("en"), so the phone shows
///     who sent the code and why, never a bare number.
/// </summary>
public sealed class OtpSender(
    ISmsGateway smsGateway,
    IEmailGateway emailGateway,
    IOptions<SmsOptions> smsOptions)
    : IOtpSender
{
    private const string DefaultLanguage = "en";

    public Task SendAsync(
        OtpChannel channel,
        string destination,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        return channel switch
        {
            OtpChannel.Phone =>
                smsGateway.SendAsync(
                    new SmsMessage(
                        destination,
                        SmsText(code)),
                    cancellationToken),

            OtpChannel.Email =>
                emailGateway.SendAsync(
                    new EmailMessage(
                        destination,
                        "Your KraftHaus verification code",
                        $"Your verification code is {code}.",
                        $"<p>Your verification code is <strong>{code}</strong>.</p>"),
                    cancellationToken),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(channel),
                    channel,
                    "Unsupported OTP channel.")
        };
    }

    private string SmsText(string code)
    {
        // The options validator guarantees at least one template, each containing {0}.
        var templates = smsOptions.Value.Templates.Otp;
        var template = templates.TryGetValue(DefaultLanguage, out var english) ? english : templates.Values.First();

        return string.Format(CultureInfo.InvariantCulture, template, code);
    }
}
