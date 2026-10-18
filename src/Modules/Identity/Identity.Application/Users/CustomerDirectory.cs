using Common.Application.Customers;
using Common.Application.Tenancy;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Users;

/// <summary><see cref="ICustomerDirectory" /> for other modules, limited to the host tenant's identity realm.</summary>
public sealed class CustomerDirectory(
    IIdentityUserRepository users,
    ICurrentTenant currentTenant)
    : ICustomerDirectory
{
    public async Task<CustomerProfile?> FindAsync(ApplicationUserId userId, CancellationToken cancellationToken)
    {
        if (!currentTenant.IsResolved)
        {
            return null;
        }

        var user = await users.FindAsync(userId, cancellationToken);
        var realm = IdentityRealm.ForHost(currentTenant.TenantId, currentTenant.IdentityScope);

        return user is null || !user.IsIn(realm) || user.Status != UserStatus.Active
            ? null
            : new CustomerProfile(user.Id, user.Phone?.Value, user.DisplayName);
    }
}
