using Common.Application.Messaging;
using Common.Application.Tenancy;
using Hangfire;

namespace Api.Infrastructure.BackgroundJobs;

/// <summary>
///     Hangfire target for every <see cref="ITenantJob" />: one run walks the active tenants and executes the job in
///     each tenant's scope. Runs never overlap (a slow run makes the next one wait, then skip), and are not retried by
///     Hangfire: the next scheduled run is the retry.
/// </summary>
public sealed class TenantJobRunner(
    ITenantScopeRunner tenants,
    IEnumerable<TenantJobDescriptor> jobs)
{
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public Task RunAsync(string jobName, CancellationToken cancellationToken)
    {
        var job = jobs.SingleOrDefault(x => string.Equals(x.Name, jobName, StringComparison.Ordinal))
                  ?? throw new InvalidOperationException($"No tenant job named '{jobName}' is registered.");

        return tenants.ForEachActiveTenantAsync(
            (scope, ct) => ((ITenantJob)scope.GetRequiredService(job.JobType)).ExecuteAsync(ct),
            cancellationToken);
    }
}
