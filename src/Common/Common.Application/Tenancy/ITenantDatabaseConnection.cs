namespace Common.Application.Tenancy;

/// <summary>
///     The connection string of the resolved tenant's own database (<c>krafthaus_t_&lt;tenant&gt;</c>, Spec 08
///     §5.1, TD-04). Tenant-scoped modules (Events, Ticketing, Payments, ...) build their <c>DbContext</c> from it.
///     Implemented by the Tenancy module so other modules can depend on it without referencing Tenancy.
/// </summary>
public interface ITenantDatabaseConnection
{
    /// <summary>True when a tenant has been resolved for the current request or job scope.</summary>
    bool IsAvailable { get; }

    /// <summary>
    ///     The resolved tenant's connection string. Throws when no tenant is resolved: tenant resolution middleware
    ///     rejects such requests before they reach a module, so reaching this without a tenant is a programming error.
    /// </summary>
    string GetConnectionString();
}
