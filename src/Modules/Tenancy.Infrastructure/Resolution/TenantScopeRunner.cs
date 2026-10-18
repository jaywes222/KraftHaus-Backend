using Common.Application.Tenancy;
using Common.Domain.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tenancy.Application.Abstractions;
using Tenancy.Domain.Tenants;

namespace Tenancy.Infrastructure.Resolution;

/// <summary>
///     <see cref="ITenantScopeRunner" /> for jobs and dispatchers: a fresh DI scope per tenant with the
///     <see cref="TenantContext" /> set, so <see cref="ITenantDatabaseConnection" /> points at that tenant's database.
/// </summary>
internal sealed partial class TenantScopeRunner(
    IServiceScopeFactory scopeFactory,
    ITenantRegistry registry,
    ILogger<TenantScopeRunner> logger)
    : ITenantScopeRunner
{
    public async Task ForEachActiveTenantAsync(
        Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        foreach (var tenant in await ActiveAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await RunAsync(tenant, work, cancellationToken);
            }
#pragma warning disable CA1031 // One tenant's failure must not starve the others; it is logged and retried next run.
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogTenantWorkFailed(logger, tenant.TenantId.Value, ex);
            }
        }
    }

    public async Task RunInTenantAsync(
        Guid tenantId,
        Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var tenant = await registry.FindByIdAsync(new TenantId(tenantId), cancellationToken);

        if (tenant is null || !IsActive(tenant))
        {
            throw new InvalidOperationException($"Tenant '{tenantId}' is unknown or not active.");
        }

        await RunAsync(tenant, work, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetActiveTenantIdsAsync(CancellationToken cancellationToken) =>
        [.. (await ActiveAsync(cancellationToken)).Select(x => x.TenantId.Value)];

    private async Task<IEnumerable<TenantDescriptor>> ActiveAsync(CancellationToken cancellationToken) =>
        (await registry.GetAllAsync(cancellationToken)).Where(IsActive);

    private static bool IsActive(TenantDescriptor tenant) =>
        tenant.Status == TenantStatus.Active && !string.IsNullOrWhiteSpace(tenant.DatabaseName);

    private async Task RunAsync(
        TenantDescriptor tenant,
        Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var tenantScopes = scope.ServiceProvider.GetRequiredService<ITenantScopeFactory>();
        using var tenantScope = await tenantScopes.CreateAsync(tenant, cancellationToken);

        await work(scope.ServiceProvider, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Background work failed for tenant {TenantId}.")]
    private static partial void LogTenantWorkFailed(ILogger logger, Guid tenantId, Exception exception);
}
