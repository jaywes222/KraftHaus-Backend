using System.Text.Json;
using Identity.Application.Auditing;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Identity.Infrastructure.Auditing;

/// <summary>
///     Writes refused Identity requests straight to <c>identity.security_audit_log</c> with a single parameterised
///     INSERT. It bypasses the EF change tracker on purpose: recording a rejection must never flush half-finished
///     aggregate changes that the refused command left in the unit of work.
/// </summary>
public sealed partial class IdentityAuditTrail(
    IdentityDbContext db,
    IdentityAuditEntryFactory entryFactory,
    ILogger<IdentityAuditTrail> logger)
    : IIdentityAuditTrail
{
    private const string InsertSql =
        $"""
         INSERT INTO "{IdentityAuditRecord.Schema}"."{IdentityAuditRecord.TableName}"
             ("Id", "OccurredAt", "Module", "Action", "Outcome", "ActorType", "ActorId", "ActorSessionId",
              "SubjectUserId", "TenantId", "TargetType", "TargetId", "ReasonCode", "TraceId", "RequestId", "Metadata")
         VALUES
             (@id, @occurredAt, @module, @action, @outcome, @actorType, @actorId, @actorSessionId,
              @subjectUserId, @tenantId, @targetType, @targetId, @reasonCode, @traceId, @requestId, @metadata)
         """;

    public async Task RecordRejectedAsync(
        IdentityAuditRejection rejection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rejection);

        try
        {
            var record = IdentityAuditRecord.From(
                entryFactory.FromRejection(rejection));

            await db.Database.ExecuteSqlRawAsync(
                InsertSql,
                Parameters(record),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Best effort by design: never replace the caller's original error with an audit failure.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRecordFailed(
                logger,
                rejection.Action,
                rejection.Error.Key,
                ex);
        }
    }

    private static NpgsqlParameter[] Parameters(IdentityAuditRecord record) =>
    [
        Parameter("id", NpgsqlDbType.Uuid, record.Id),
        Parameter("occurredAt", NpgsqlDbType.TimestampTz, record.OccurredAt),
        Parameter("module", NpgsqlDbType.Varchar, record.Module),
        Parameter("action", NpgsqlDbType.Varchar, record.Action),
        Parameter("outcome", NpgsqlDbType.Varchar, record.Outcome),
        Parameter("actorType", NpgsqlDbType.Varchar, record.ActorType),
        Parameter("actorId", NpgsqlDbType.Varchar, record.ActorId),
        Parameter("actorSessionId", NpgsqlDbType.Varchar, record.ActorSessionId),
        Parameter("subjectUserId", NpgsqlDbType.Uuid, record.SubjectUserId),
        Parameter("tenantId", NpgsqlDbType.Uuid, record.TenantId),
        Parameter("targetType", NpgsqlDbType.Varchar, record.TargetType),
        Parameter("targetId", NpgsqlDbType.Varchar, record.TargetId),
        Parameter("reasonCode", NpgsqlDbType.Varchar, record.ReasonCode),
        Parameter("traceId", NpgsqlDbType.Varchar, record.TraceId),
        Parameter("requestId", NpgsqlDbType.Varchar, record.RequestId),
        Parameter("metadata", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(record.Metadata))
    ];

    private static NpgsqlParameter Parameter(
        string name,
        NpgsqlDbType type,
        object? value) =>
        new(name, type)
        {
            Value = value ?? DBNull.Value
        };

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to record identity audit rejection {Action} ({ReasonCode}).")]
    private static partial void LogRecordFailed(
        ILogger logger,
        string action,
        string reasonCode,
        Exception exception);
}
