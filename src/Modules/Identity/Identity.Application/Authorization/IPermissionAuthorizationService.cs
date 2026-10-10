using Common.Domain.StronglyTypedIds;
using Identity.Domain.Authorization;

namespace Identity.Application.Authorization;

public interface IPermissionAuthorizationService
{
    Task<bool> HasPermissionAsync(
        ApplicationUserId userId,
        string permission,
        PermissionScope scope,
        CancellationToken cancellationToken);
}
