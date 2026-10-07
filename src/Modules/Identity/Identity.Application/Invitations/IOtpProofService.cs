using Identity.Domain.Common;
using Identity.Domain.Otp;

namespace Identity.Application.Invitations;

public interface IOtpProofService
{
    Task<bool> VerifyAsync(
        OtpChannel channel,
        string destination,
        string code,
        CancellationToken cancellationToken);
}
