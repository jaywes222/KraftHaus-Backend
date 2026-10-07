using Tenancy.Domain.Common;

namespace Tenancy.Application.Abstractions;

public interface ITenantResolutionService
{
    Task<Common.Domain.ResultMonad.Result<TenantDescriptor>> ResolveAsync(
        string host,
        Guid? tid,
        CancellationToken cancellationToken);
}
