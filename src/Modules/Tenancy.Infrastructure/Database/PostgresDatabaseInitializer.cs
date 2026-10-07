using Common.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Tenancy.Infrastructure.Options;
using Tenancy.Infrastructure.Persistence;

namespace Tenancy.Infrastructure.Database;

public sealed partial class DatabaseInitializer(
    IModuleDatabaseRegistry moduleDatabaseRegistry,
    IOptions<TenancyDatabaseOptions> options,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    private const int MaxAttempts = 3;

    private readonly TenancyDatabaseOptions _options = options.Value;

    public Task InitializePlatformAsync(
        CancellationToken cancellationToken)
        => InitializeAsync(
            DatabaseScope.Platform,
            null,
            GetPlatformDatabaseName(),
            _options.PlatformConnectionString,
            cancellationToken);

    public Task InitializeTenantAsync(
        Guid tenantId,
        string databaseName,
        string connectionString,
        CancellationToken cancellationToken)
        => InitializeAsync(
            DatabaseScope.Tenant,
            tenantId,
            databaseName,
            connectionString,
            cancellationToken);

    private async Task InitializeAsync(
        DatabaseScope scope,
        Guid? tenantId,
        string databaseName,
        string connectionString,
        CancellationToken cancellationToken)
    {
        ValidateDatabaseName(databaseName);

        await using var admin =
            await OpenAdminConnectionAsync(cancellationToken);

        var platformDatabaseName = GetPlatformDatabaseName();

        await EnsureDatabaseExistsAsync(
            admin,
            platformDatabaseName,
            cancellationToken);

        await EnsureInfrastructureTablesAsync(
            cancellationToken);

        await using var advisoryLock =
            await PostgresAdvisoryLock.AcquireAsync(
                admin,
                $"krafthaus:database:{databaseName}",
                cancellationToken);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await RecordProvisioningAttemptAsync(
                tenantId,
                databaseName,
                attempt,
                ProvisioningAttemptStatus.Started,
                null,
                cancellationToken);

            try
            {
                await EnsureDatabaseExistsAsync(
                    admin,
                    databaseName,
                    cancellationToken);

                await using var target =
                    new NpgsqlConnection(connectionString);

                await target.OpenAsync(cancellationToken);

                await EnsureIdentityTablesAsync(
                    target,
                    cancellationToken);

                await EnsureIdentityAsync(
                    target,
                    scope,
                    tenantId,
                    databaseName,
                    cancellationToken);

                await MigrateAsync(
                    scope,
                    connectionString,
                    cancellationToken);

                await RecordProvisioningAttemptAsync(
                    tenantId,
                    databaseName,
                    attempt,
                    ProvisioningAttemptStatus.Succeeded,
                    null,
                    cancellationToken);

                LogDatabaseInitialized(
                    logger,
                    databaseName,
                    scope,
                    attempt);

                return;
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                await RecordProvisioningAttemptAsync(
                    tenantId,
                    databaseName,
                    attempt,
                    ProvisioningAttemptStatus.Failed,
                    exception.Message,
                    cancellationToken);

                if (attempt == MaxAttempts)
                {
                    throw new InvalidOperationException(
                        $"Database initialization failed after " +
                        $"{MaxAttempts} attempts.",
                        exception);
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(
                        Math.Pow(2, attempt - 1)),
                    cancellationToken);
            }
        }

        // The loop either returns after success or throws on the
        // final failed attempt.
        throw new InvalidOperationException(
            $"Database initialization failed after {MaxAttempts} attempts.");
    }

    private async Task<NpgsqlConnection> OpenAdminConnectionAsync(
        CancellationToken cancellationToken)
    {
        var builder =
            new NpgsqlConnectionStringBuilder(
                _options.AdminConnectionString)
            {
                Database = "postgres"
            };

        var connection =
            new NpgsqlConnection(builder.ConnectionString);

        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    private static async Task EnsureDatabaseExistsAsync(
        NpgsqlConnection admin,
        string databaseName,
        CancellationToken cancellationToken)
    {
        var safeDatabaseName =
            ValidateDatabaseName(databaseName);

        await using var exists = new NpgsqlCommand(
            """
            SELECT 1
            FROM pg_database
            WHERE datname = @name
            """,
            admin);

        exists.Parameters.AddWithValue(
            "name",
            databaseName);

        var present =
            await exists.ExecuteScalarAsync(
                cancellationToken) is not null;

        if (present)
        {
            return;
        }

        /*
         * PostgreSQL parameters cannot be used for identifiers such as
         * database names. The identifier is therefore:
         *
         * 1. Strictly validated by ValidateDatabaseName().
         * 2. Quoted using NpgsqlCommandBuilder.QuoteIdentifier().
         *
         * The resulting value is safe to embed as a PostgreSQL identifier.
         */
#pragma warning disable CA2100
        await using var create = new NpgsqlCommand(
            $"CREATE DATABASE {safeDatabaseName}",
            admin);
#pragma warning restore CA2100

        await create.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private async Task EnsureInfrastructureTablesAsync(
        CancellationToken cancellationToken)
    {
        await using var platform =
            new NpgsqlConnection(
                _options.PlatformConnectionString);

        await platform.OpenAsync(
            cancellationToken);

        await EnsureIdentityTablesAsync(
            platform,
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                CREATE TABLE IF NOT EXISTS infra.provisioning_attempt
                (
                    id uuid PRIMARY KEY,
                    tenant_id uuid NOT NULL,
                    database_name varchar(63) NOT NULL,
                    attempt integer NOT NULL,
                    status text NOT NULL,
                    error varchar(4000) NULL,
                    started_on timestamptz NOT NULL,
                    completed_on timestamptz NULL,

                    CONSTRAINT ux_provisioning_attempt
                        UNIQUE (tenant_id, attempt)
                );
                """,
                platform);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task EnsureIdentityTablesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command =
            new NpgsqlCommand(
                """
                CREATE SCHEMA IF NOT EXISTS infra;

                CREATE TABLE IF NOT EXISTS infra.database_identity
                (
                    id uuid PRIMARY KEY,
                    kind text NOT NULL,
                    tenant_id uuid NULL,
                    database_name varchar(63) NOT NULL,
                    schema_version varchar(32) NOT NULL,
                    created_on timestamptz NOT NULL,

                    CONSTRAINT ux_database_identity_scope
                        UNIQUE (kind, tenant_id)
                );
                """,
                connection);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task EnsureIdentityAsync(
        NpgsqlConnection connection,
        DatabaseScope scope,
        Guid? tenantId,
        string databaseName,
        CancellationToken cancellationToken)
    {
        await using var read =
            new NpgsqlCommand(
                """
                SELECT
                    kind,
                    tenant_id,
                    database_name
                FROM infra.database_identity
                LIMIT 1
                """,
                connection);

        await using var reader =
            await read.ExecuteReaderAsync(
                cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            var existingKind =
                reader.GetString(0);

            var existingTenant =
                await reader.IsDBNullAsync(
                    1,
                    cancellationToken)
                    ? (Guid?)null
                    : reader.GetGuid(1);

            var existingDatabase =
                reader.GetString(2);

            DatabaseIdentityGuard.Validate(
                existingKind,
                existingTenant,
                existingDatabase,
                scope,
                tenantId,
                databaseName);

            return;
        }

        await reader.DisposeAsync();

        await using var insert =
            new NpgsqlCommand(
                """
                INSERT INTO infra.database_identity
                (
                    id,
                    kind,
                    tenant_id,
                    database_name,
                    schema_version,
                    created_on
                )
                VALUES
                (
                    @id,
                    @kind,
                    @tenant_id,
                    @database_name,
                    '1',
                    now()
                )
                """,
                connection);

        insert.Parameters.AddWithValue(
            "id",
            Guid.CreateVersion7());

        insert.Parameters.AddWithValue(
            "kind",
            scope == DatabaseScope.Platform
                ? "Platform"
                : "Tenant");

        insert.Parameters.AddWithValue(
            "tenant_id",
            tenantId.HasValue
                ? tenantId.Value
                : DBNull.Value);

        insert.Parameters.AddWithValue(
            "database_name",
            databaseName);

        await insert.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private async Task MigrateAsync(
        DatabaseScope scope,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var descriptors =
            moduleDatabaseRegistry
                .GetAll()
                .Where(x => x.Scope == scope)
                .OrderBy(x => x.ModuleName);

        foreach (var descriptor in descriptors)
        {
            await using var db =
                descriptor.CreateDbContext(
                    connectionString);

            await db.Database.MigrateAsync(
                cancellationToken);
        }
    }

    private async Task RecordProvisioningAttemptAsync(
        Guid? tenantId,
        string databaseName,
        int attempt,
        ProvisioningAttemptStatus status,
        string? error,
        CancellationToken cancellationToken)
    {
        if (!tenantId.HasValue)
        {
            return;
        }

        await using var connection =
            new NpgsqlConnection(
                _options.PlatformConnectionString);

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            new NpgsqlCommand(
                """
                INSERT INTO infra.provisioning_attempt
                (
                    id,
                    tenant_id,
                    database_name,
                    attempt,
                    status,
                    error,
                    started_on,
                    completed_on
                )
                VALUES
                (
                    @id,
                    @tenant_id,
                    @database_name,
                    @attempt,
                    @status,
                    @error,
                    now(),
                    CASE
                        WHEN @status = 'Succeeded'
                          OR @status = 'Failed'
                        THEN now()
                        ELSE NULL
                    END
                )
                ON CONFLICT (tenant_id, attempt)
                DO UPDATE SET
                    status = EXCLUDED.status,
                    error = EXCLUDED.error,
                    completed_on = EXCLUDED.completed_on
                """,
                connection);

        command.Parameters.AddWithValue(
            "id",
            Guid.CreateVersion7());

        command.Parameters.AddWithValue(
            "tenant_id",
            tenantId.Value);

        command.Parameters.AddWithValue(
            "database_name",
            databaseName);

        command.Parameters.AddWithValue(
            "attempt",
            attempt);

        command.Parameters.AddWithValue(
            "status",
            status.ToString());

        command.Parameters.AddWithValue(
            "error",
            (object?)error ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static string ValidateDatabaseName(
        string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException(
                "Database name is required.",
                nameof(databaseName));
        }

        if (databaseName.Length > 63)
        {
            throw new ArgumentException(
                "PostgreSQL database names cannot exceed 63 characters.",
                nameof(databaseName));
        }

        if (databaseName.Any(
            c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
        {
            throw new ArgumentException(
                "Database name may contain only ASCII letters, digits, and underscores.",
                nameof(databaseName));
        }

        using var commandBuilder =
            new NpgsqlCommandBuilder();

        return commandBuilder.QuoteIdentifier(
            databaseName);
    }

    private string GetPlatformDatabaseName()
        => new NpgsqlConnectionStringBuilder(
            _options.PlatformConnectionString).Database
           ?? throw new InvalidOperationException(
               "PlatformConnectionString must specify a database name.");

    [LoggerMessage(
        LogLevel.Information,
        "Database {DatabaseName} initialized for {Scope} on attempt {Attempt}.")]
    private static partial void LogDatabaseInitialized(
        ILogger logger,
        string databaseName,
        DatabaseScope scope,
        int attempt);
}

internal sealed class PostgresAdvisoryLock : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly long _key;

    private PostgresAdvisoryLock(
        NpgsqlConnection connection,
        long key)
    {
        _connection = connection;
        _key = key;
    }

    public static async Task<PostgresAdvisoryLock> AcquireAsync(
        NpgsqlConnection connection,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command =
            new NpgsqlCommand(
                "SELECT hashtextextended(@key, 0)",
                connection);

        command.Parameters.AddWithValue(
            "key",
            name);

        var key =
            (long)(await command.ExecuteScalarAsync(
                cancellationToken))!;

        await using var acquire =
            new NpgsqlCommand(
                "SELECT pg_advisory_lock(@key)",
                connection);

        acquire.Parameters.AddWithValue(
            "key",
            key);

        await acquire.ExecuteNonQueryAsync(
            cancellationToken);

        return new PostgresAdvisoryLock(
            connection,
            key);
    }

    public async ValueTask DisposeAsync()
    {
        await using var command =
            new NpgsqlCommand(
                "SELECT pg_advisory_unlock(@key)",
                _connection);

        command.Parameters.AddWithValue(
            "key",
            _key);

        await command.ExecuteNonQueryAsync();
    }
}
