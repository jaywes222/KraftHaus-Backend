namespace Common.Application.Messaging;

/// <summary>
///     A recurring job that runs once per active tenant (release expired holds, poll stale payments, installment
///     reminders, ...). Resolved from a tenant scope on every run, so it may depend on tenant-scoped services. Register
///     it with <c>AddTenantJob&lt;TJob&gt;(name, cron)</c>.
/// </summary>
public interface ITenantJob
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}

/// <summary>A registered <see cref="ITenantJob" />: stable recurring-job id, cron (UTC) and implementation type.</summary>
public sealed record TenantJobDescriptor(string Name, string Cron, Type JobType);
