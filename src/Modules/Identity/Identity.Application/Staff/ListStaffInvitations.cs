using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Identity.Application.Invitations;
using Identity.Domain.Common;

namespace Identity.Application.Staff;

public sealed class ListStaffInvitationsService(
    IStaffInvitationRepository invitations,
    TimeProvider time,
    ICurrentTenant currentTenant)
{
    public async Task<Result<IReadOnlyList<StaffInvitationResult>>> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!currentTenant.IsResolved)
        {
            return IdentityErrors.InvalidState("TenantUnresolved", "Listing invitations requires a tenant host.");
        }

        var now = time.GetUtcNow();
        var pending = await invitations.ListPendingAsync(currentTenant.TenantId, cancellationToken);

        return Result<IReadOnlyList<StaffInvitationResult>>.Success(
            [.. pending.Select(x => StaffInvitationResult.From(x, now))]);
    }
}
