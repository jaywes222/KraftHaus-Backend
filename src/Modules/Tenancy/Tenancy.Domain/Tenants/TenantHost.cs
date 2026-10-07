using Common.Domain.Entities;
using Tenancy.Domain.Common;

namespace Tenancy.Domain.Tenants;

public sealed class TenantHost(TenantHostId id) : AuditableEntity<TenantHostId>(id)
{
    private TenantHost() : this(new TenantHostId(Guid.Empty))
    {
    }

    public required TenantId TenantId { get; init; }

    public HostName HostName { get; private set; } = default!;

    public HostSurface Surface { get; private set; } = default!;

    public bool IsPrimary { get; private set; }

    public bool Enabled { get; private set; }

    public static TenantHost Create(
        TenantHostId id,
        TenantId tenantId,
        HostName hostName,
        HostSurface surface,
        bool isPrimary)
        => new(id)
        {
            TenantId = tenantId,
            HostName = hostName,
            Surface = surface,
            IsPrimary = isPrimary,
            Enabled = true
        };

    public void Disable() => Enabled = false;

    public void Enable() => Enabled = true;
}
