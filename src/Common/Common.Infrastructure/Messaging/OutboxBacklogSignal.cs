using System.Globalization;
using System.Text.RegularExpressions;
using Common.Application.Operations;
using Common.Application.Tenancy;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Common.Infrastructure.Messaging;

/// <summary>
///     Operator alert for integration events that are not getting through (Spec 08 §14 "outbox lag, dead-lettered
///     messages"): parked messages, and messages still waiting well past their retry time, in every module outbox of
///     the current tenant.
/// </summary>
public sealed partial class OutboxBacklogSignal(
    IEnumerable<ModuleOutboxDescriptor> outboxes,
    ITenantDatabaseConnection database,
    IOptions<OperationalAlertOptions> options,
    TimeProvider time)
    : IOperationalSignal
{
    public async Task<IReadOnlyList<OperationalIssue>> CheckAsync(CancellationToken cancellationToken)
    {
        var schemas = outboxes.Select(x => x.Schema).Distinct(StringComparer.Ordinal).Where(x => SafeIdentifier().IsMatch(x)).ToList();
        if (schemas.Count == 0)
        {
            return [];
        }

        var parked = 0;
        var late = 0;
        var cutoff = time.GetUtcNow().AddMinutes(-options.Value.BacklogMinutes);

        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        foreach (var schema in schemas)
        {
#pragma warning disable CA2100 // The schema is a validated code constant from ModuleOutboxDescriptor, never input.
            await using var command = new NpgsqlCommand(
                $"""
                 SELECT count(*) FILTER (WHERE "DeadLetteredAt" IS NOT NULL),
                        count(*) FILTER (WHERE "DeadLetteredAt" IS NULL AND "AvailableAt" < @cutoff)
                 FROM {schema}.outbox_message
                 WHERE "ProcessedAt" IS NULL
                 """,
                connection);
#pragma warning restore CA2100
            command.Parameters.AddWithValue("cutoff", cutoff);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                parked += (int)reader.GetInt64(0);
                late += (int)reader.GetInt64(1);
            }
        }

        var issues = new List<OperationalIssue>();
        if (parked > 0)
        {
            issues.Add(new OperationalIssue("outbox.parked", string.Create(CultureInfo.InvariantCulture,
                $"{parked} event(s) failed delivery and are parked: tickets or orders may be waiting on them.")));
        }

        if (late > 0)
        {
            issues.Add(new OperationalIssue("outbox.backlog", string.Create(CultureInfo.InvariantCulture,
                $"{late} event(s) are more than {options.Value.BacklogMinutes} min late (dispatcher stalled?).")));
        }

        return issues;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.None, 50)]
    private static partial Regex SafeIdentifier();
}
