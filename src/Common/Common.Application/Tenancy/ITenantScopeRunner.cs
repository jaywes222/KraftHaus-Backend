namespace Common.Application.Tenancy;

/// <summary>
///     Runs work inside a tenant's scope outside an HTTP request (background jobs, the outbox dispatcher, webhooks
///     whose tenant comes from the payload). Each call gets its own DI scope with the tenant resolved, so tenant-scoped
///     <c>DbContext</c>s connect to that tenant's database. Implemented by the Tenancy module.
/// </summary>
public interface ITenantScopeRunner
{
    /// <summary>
    ///     Runs <paramref name="work" /> once per active, provisioned tenant, one at a time. A failure in one tenant
    ///     is logged and does not stop the others.
    /// </summary>
    Task ForEachActiveTenantAsync(
        Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken);

    /// <summary>Runs <paramref name="work" /> in one tenant's scope. Throws when the tenant is unknown or not active.</summary>
    Task RunInTenantAsync(
        Guid tenantId,
        Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken);

    /// <summary>Ids of the active, provisioned tenants.</summary>
    Task<IReadOnlyList<Guid>> GetActiveTenantIdsAsync(CancellationToken cancellationToken);
}
