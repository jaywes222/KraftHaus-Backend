namespace Common.Domain.Tenancy;

/// <summary>
///     Where a tenant keeps its customer identities (Spec 08 §5.1): the shared platform identity store, or the
///     tenant's own scope.
/// </summary>
public enum IdentityScope
{
    Platform = 1,
    Tenant = 2
}
