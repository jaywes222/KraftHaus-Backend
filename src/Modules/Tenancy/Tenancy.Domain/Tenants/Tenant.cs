using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Tenancy.Domain.Common;

namespace Tenancy.Domain.Tenants;

public sealed class Tenant(TenantId id) : AggregateRoot<TenantId>(id)
{
    private readonly List<TenantHost> _hosts = [];
    private readonly List<PolicyVersion> _policyVersions = [];

    private Tenant() : this(new TenantId(Guid.Empty))
    {
    }

    public TenantSlug Slug { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public IdentityScope IdentityScope { get; private set; }

    public TenantStatus Status { get; private set; }

    public string? DatabaseName { get; private set; }

    public IReadOnlyCollection<TenantHost> Hosts =>
        _hosts.AsReadOnly();

    public IReadOnlyCollection<PolicyVersion> PolicyVersions =>
        _policyVersions.AsReadOnly();

    public static Result<Tenant> Create(
        TenantId id,
        TenantSlug slug,
        string name,
        IdentityScope identityScope = IdentityScope.Tenant)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new Error
            {
                Key = "Tenant.NameRequired",
                Description = "Tenant name is required."
            };
        }

        return new Tenant(id)
        {
            Slug = slug,
            Name = name.Trim(),
            IdentityScope = identityScope,
            Status = TenantStatus.Provisioning
        };
    }

    public Result AddHost(TenantHost host)
    {
        if (host.TenantId != Id)
        {
            return new Error
            {
                Key = "Tenant.HostTenantMismatch",
                Description = "The host belongs to a different tenant."
            };
        }

        if (_hosts.Any(x => x.HostName == host.HostName))
        {
            return Error.ViolatesUniqueConstraint(
                nameof(TenantHost.HostName),
                host.HostName.Value);
        }

        if (host.IsPrimary && _hosts.Any(x => x.IsPrimary && x.Enabled))
        {
            return new Error
            {
                Key = "Tenant.PrimaryHostExists",
                Description = "A tenant can have only one enabled primary host."
            };
        }

        _hosts.Add(host);
        Version++;

        return Result.Success;
    }

    public Result AddPolicyVersion(PolicyVersion policyVersion)
    {
        if (policyVersion.TenantId != Id)
        {
            return new Error
            {
                Key = "Tenant.PolicyTenantMismatch",
                Description = "The policy version belongs to a different tenant."
            };
        }

        if (_policyVersions.Any(x =>
            x.PolicyKey == policyVersion.PolicyKey &&
            x.VersionNumber == policyVersion.VersionNumber))
        {
            return Error.ViolatesUniqueConstraint(
                "PolicyVersion",
                $"{policyVersion.PolicyKey}:{policyVersion.VersionNumber}");
        }

        _policyVersions.Add(policyVersion);
        Version++;

        return Result.Success;
    }

    public Result MarkProvisioned(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return new Error
            {
                Key = "Tenant.DatabaseNameRequired",
                Description = "A tenant database name is required."
            };
        }

        DatabaseName = databaseName.Trim();
        Status = TenantStatus.Active;
        Version++;

        return Result.Success;
    }

    public void MarkProvisioningFailed()
    {
        Status = TenantStatus.Failed;
        Version++;
    }

    public void Suspend()
    {
        Status = TenantStatus.Suspended;
        Version++;
    }

    public void Disable()
    {
        Status = TenantStatus.Disabled;
        Version++;
    }
}
