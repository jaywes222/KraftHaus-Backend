using Common.Domain.Entities;
using Tenancy.Domain.Common;

namespace Tenancy.Domain.Tenants;

public sealed class PolicyVersion(PolicyVersionId id) : AuditableEntity<PolicyVersionId>(id)
{
    private PolicyVersion() : this(new PolicyVersionId(Guid.Empty))
    {
    }

    public required TenantId TenantId { get; init; }

    public string PolicyKey { get; private set; } = default!;

    public int VersionNumber { get; private set; }

    public string PolicyHash { get; private set; } = default!;

    public DateTimeOffset EffectiveFrom { get; private set; }

    public static PolicyVersion Create(
        PolicyVersionId id,
        TenantId tenantId,
        string policyKey,
        int versionNumber,
        string policyHash,
        DateTimeOffset effectiveFrom)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyHash);

        return new PolicyVersion(id)
        {
            TenantId = tenantId,
            PolicyKey = policyKey.Trim(),
            VersionNumber = versionNumber,
            PolicyHash = policyHash.Trim(),
            EffectiveFrom = effectiveFrom
        };
    }
}
