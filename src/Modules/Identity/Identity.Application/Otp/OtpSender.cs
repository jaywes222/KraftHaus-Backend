using Common.Application.Notifications;
using Identity.Application.Abstractions;
using Identity.Domain.Otp;

namespace Identity.Application.Otp;

public sealed class OtpSender(
    ISmsGateway smsGateway,
    IEmailGateway emailGateway)
    : IOtpSender
{
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
                        code),
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
}
