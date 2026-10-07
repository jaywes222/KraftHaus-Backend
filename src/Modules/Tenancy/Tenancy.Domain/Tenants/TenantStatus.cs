namespace Tenancy.Domain.Tenants;

public enum TenantStatus
{
    Provisioning = 1,
    Active = 2,
    Suspended = 3,
    Disabled = 4,
    Failed = 5
}
