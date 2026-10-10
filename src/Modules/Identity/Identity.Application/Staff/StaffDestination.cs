using Common.Domain.ResultMonad;
using Identity.Domain.Common;
using Identity.Domain.Invitations;
using Identity.Domain.Users;

namespace Identity.Application.Staff;

/// <summary>Normalises a phone number / e-mail address the same way sign-in does, so the invitation matches it.</summary>
public static class StaffDestination
{
    public static Result<string> Normalize(InvitationChannel channel, string destination)
    {
        switch (channel)
        {
            case InvitationChannel.Phone:
                {
                    var phone = PhoneNumber.Create(destination);
                    return phone.IsFailure ? phone.Error! : phone.Value!.Value;
                }

            case InvitationChannel.Email:
                {
                    var email = EmailAddress.Create(destination);
                    return email.IsFailure ? email.Error! : email.Value!.Value;
                }

            default:
                return IdentityErrors.Invalid("Channel", "Unsupported invitation channel.");
        }
    }
}
