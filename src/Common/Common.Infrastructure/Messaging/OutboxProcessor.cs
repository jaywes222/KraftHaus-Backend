using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Common.Application.Messaging;
using Common.Application.Tenancy;
using Common.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Common.Infrastructure.Messaging;

/// <summary>A module schema whose <c>outbox_message</c> table the dispatcher drains in every tenant database.</summary>
public sealed record ModuleOutboxDescriptor(string Schema);

/// <summary>
///     Drains the outboxes of the current tenant (Spec 08 §9.1): locks a batch with <c>FOR UPDATE SKIP LOCKED</c>,
///     delivers each message to its <see cref="IIntegrationEventHandler{TEvent}" />s in a fresh tenant scope, then marks
///     it processed, or schedules a retry with exponential backoff and parks it after <c>MaxAttempts</c>.
/// </summary>
public sealed partial class OutboxProcessor(
    IEnumerable<ModuleOutboxDescriptor> outboxes,
    ITenantScopeRunner tenants,
    IOptions<OutboxOptions> options,
    TimeProvider time,
    ILogger<OutboxProcessor> logger)
{
    private static readonly ActivitySource ActivitySource = new("Krafthaus.Outbox");

    private static readonly MethodInfo DeliverMethod =
        typeof(OutboxProcessor).GetMethod(nameof(DeliverTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Processes one batch per module outbox of the tenant in <paramref name="tenantScope" />. Returns rows handled.</summary>
    public async Task<int> ProcessTenantAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);

        var tenant = tenantScope.GetRequiredService<ICurrentTenant>();
        var connectionString = tenantScope.GetRequiredService<ITenantDatabaseConnection>().GetConnectionString();
        var handled = 0;

        foreach (var outbox in outboxes.DistinctBy(x => x.Schema, StringComparer.Ordinal))
        {
            handled += await ProcessOutboxAsync(tenant.TenantId.Value, connectionString, outbox.Schema, cancellationToken);
        }

        return handled;
    }

    private async Task<int> ProcessOutboxAsync(
        Guid tenantId,
        string connectionString,
        string schema,
        CancellationToken cancellationToken)
    {
        if (!SafeIdentifier().IsMatch(schema))
        {
            throw new InvalidOperationException($"Outbox schema '{schema}' is not a plain lower-case identifier.");
        }

        var settings = options.Value;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var batch = new List<(Guid Id, string Type, string Payload, int Attempts, string? TraceParent)>();

#pragma warning disable CA2100 // The schema is a validated code constant from ModuleOutboxDescriptor, never input.
        await using (var select = new NpgsqlCommand(
                         $"""
                          SELECT "Id", "Type", "Payload"::text, "Attempts", "TraceParent"
                          FROM {schema}.outbox_message
                          WHERE "ProcessedAt" IS NULL AND "DeadLetteredAt" IS NULL AND "AvailableAt" <= @now
                          ORDER BY "CreatedAt"
                          LIMIT @batch
                          FOR UPDATE SKIP LOCKED
                          """,
                         connection,
                         transaction))
        {
            select.Parameters.AddWithValue("now", time.GetUtcNow());
            select.Parameters.AddWithValue("batch", settings.BatchSize);

            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                batch.Add((
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4)));
            }
        }

        foreach (var message in batch)
        {
            var error = await DeliverAsync(tenantId, message.Type, message.Payload, message.TraceParent, cancellationToken);
            var now = time.GetUtcNow();

            await using var update = new NpgsqlCommand(string.Empty, connection, transaction);
            update.Parameters.AddWithValue("id", message.Id);
            update.Parameters.AddWithValue("now", now);

            if (error is null)
            {
                update.CommandText =
                    $"""UPDATE {schema}.outbox_message SET "ProcessedAt" = @now, "LastError" = NULL WHERE "Id" = @id""";
            }
            else
            {
                var attempts = message.Attempts + 1;
                var parked = attempts >= settings.MaxAttempts;
                var delay = Math.Min(
                    settings.MaxRetryDelaySeconds,
                    settings.BaseRetryDelaySeconds * Math.Pow(2, Math.Min(attempts - 1, 20)));

                update.CommandText =
                    $"""
                     UPDATE {schema}.outbox_message
                     SET "Attempts" = @attempts, "LastError" = @error, "AvailableAt" = @retryAt,
                         "DeadLetteredAt" = CASE WHEN @parked THEN @now ELSE NULL END
                     WHERE "Id" = @id
                     """;
                update.Parameters.AddWithValue("attempts", attempts);
                update.Parameters.AddWithValue("error", error.Length <= OutboxMessage.ErrorMaxLength ? error : error[..OutboxMessage.ErrorMaxLength]);
                update.Parameters.AddWithValue("retryAt", now.AddSeconds(delay));
                update.Parameters.AddWithValue("parked", parked);

                if (parked)
                {
                    LogDeadLettered(logger, message.Type, message.Id, tenantId, attempts);
                }
                else
                {
                    LogDeliveryFailed(logger, message.Type, message.Id, tenantId, attempts);
                }
            }

            await update.ExecuteNonQueryAsync(cancellationToken);
        }
#pragma warning restore CA2100

        await transaction.CommitAsync(cancellationToken);

        return batch.Count;
    }

    /// <summary>Delivers one message in a fresh tenant scope. Returns <c>null</c> on success, or the failure text.</summary>
    private async Task<string?> DeliverAsync(
        Guid tenantId,
        string typeName,
        string payload,
        string? traceParent,
        CancellationToken cancellationToken)
    {
        var type = IntegrationEventTypes.Find(typeName);
        if (type is null)
        {
            return $"Unknown integration event type '{typeName}'.";
        }

        ActivityContext.TryParse(traceParent, null, out var parent);
        using var activity = ActivitySource.StartActivity($"Outbox.Deliver.{typeName}", ActivityKind.Consumer, parent);

        try
        {
            var @event = (IntegrationEvent?)JsonSerializer.Deserialize(payload, type, IntegrationEventTypes.SerializerOptions)
                         ?? throw new JsonException("The payload deserialised to null.");

            await tenants.RunInTenantAsync(
                tenantId,
                (scope, ct) => (Task)DeliverMethod.MakeGenericMethod(type).Invoke(null, [scope, @event, ct])!,
                cancellationToken);

            return null;
        }
#pragma warning disable CA1031 // A failing handler must not stop the batch; the message is retried with backoff.
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private static async Task DeliverTypedAsync<TEvent>(
        IServiceProvider scope,
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : IntegrationEvent
    {
        foreach (var handler in scope.GetServices<IIntegrationEventHandler<TEvent>>())
        {
            await handler.HandleAsync(@event, cancellationToken);
        }
    }

    [GeneratedRegex("^[a-z_]{1,63}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SafeIdentifier();

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Delivering {EventType} {MessageId} for tenant {TenantId} failed (attempt {Attempts}); it will be retried.")]
    private static partial void LogDeliveryFailed(ILogger logger, string eventType, Guid messageId, Guid tenantId, int attempts);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Parked {EventType} {MessageId} for tenant {TenantId} after {Attempts} failed attempts.")]
    private static partial void LogDeadLettered(ILogger logger, string eventType, Guid messageId, Guid tenantId, int attempts);
}

/// <summary>
///     Hosted loop that drains every tenant's outboxes: immediately when <see cref="IOutboxSignal" /> fires after a
///     commit, otherwise every <c>PollIntervalMilliseconds</c>.
/// </summary>
internal sealed partial class OutboxDispatcher(
    OutboxSignal signal,
    ITenantScopeRunner tenants,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger)
    : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromMilliseconds(options.Value.PollIntervalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await tenants.ForEachActiveTenantAsync(
                    (scope, ct) => scope.GetRequiredService<OutboxProcessor>().ProcessTenantAsync(scope, ct),
                    stoppingToken);
            }
#pragma warning disable CA1031 // The loop must survive a registry or database outage and try again.
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogScanFailed(logger, ex);
            }

            try
            {
                await signal.WaitAsync(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox scan failed; retrying on the next interval.")]
    private static partial void LogScanFailed(ILogger logger, Exception exception);
}
