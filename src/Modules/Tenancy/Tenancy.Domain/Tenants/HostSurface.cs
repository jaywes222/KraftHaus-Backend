namespace Tenancy.Domain.Tenants;

[Flags]
public enum HostSurface
{
    None = 0,
    Client = 1,
    Admin = 2,
    Ops = 4,
    Display = 8
}
