using Common.Application.Notifications;
using Identity.Domain.Invitations;

namespace Identity.Application.Staff;

/// <summary>Tells an invited person how to accept. Contains no secret: acceptance still needs a fresh OTP.</summary>
public interface IStaffInvitationNotifier
{
    Task NotifyAsync(StaffInvitation invitation, CancellationToken cancellationToken);
}

public sealed class StaffInvitationNotifier(
    ISmsGateway smsGateway,
    IEmailGateway emailGateway)
    : IStaffInvitationNotifier
{
    public Task NotifyAsync(StaffInvitation invitation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        var text =
            $"You have been invited to KraftHaus as {invitation.Role}. " +
            $"Sign in, then accept invitation {invitation.Id.Value:D} before {invitation.ExpiresAt:yyyy-MM-dd}.";

        return invitation.Channel switch
        {
            InvitationChannel.Phone =>
                smsGateway.SendAsync(new SmsMessage(invitation.Destination, text), cancellationToken),

            InvitationChannel.Email =>
                emailGateway.SendAsync(
                    new EmailMessage(
                        invitation.Destination,
                        "You have been invited to KraftHaus",
                        text,
                        $"<p>{System.Net.WebUtility.HtmlEncode(text)}</p>"),
                    cancellationToken),

            _ => throw new ArgumentOutOfRangeException(nameof(invitation), invitation.Channel, "Unsupported channel.")
        };
    }
}
