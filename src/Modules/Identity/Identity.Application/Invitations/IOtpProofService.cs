using Common.Domain.Tenancy;
using Identity.Domain.Common;
using Identity.Domain.Otp;

namespace Identity.Application.Invitations;

public interface IOtpProofService
{
    Task<bool> VerifyAsync(
        TenantId tenantId,
        OtpChannel channel,
        string destination,
        string code,
        CancellationToken cancellationToken);
}
