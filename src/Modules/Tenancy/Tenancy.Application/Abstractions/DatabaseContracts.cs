using Microsoft.EntityFrameworkCore;

namespace Tenancy.Application.Abstractions;

public enum DatabaseIdentityKind
{
    Platform = 1,
    Tenant = 2
}

public interface ITenantProvisioner
{
    Task<Common.Domain.ResultMonad.Result> ProvisionAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Common.Domain.ResultMonad.Result> InitializeAllAsync(CancellationToken cancellationToken);
}
