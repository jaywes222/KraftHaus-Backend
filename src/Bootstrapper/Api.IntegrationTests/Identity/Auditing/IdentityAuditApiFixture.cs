using System.Diagnostics;
using Common.Domain.Tenancy;
using Identity.Application.Abstractions;
using Identity.Domain.Otp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Tenancy.Application.Abstractions;
using Tenancy.Domain.Common;
using Tenancy.Domain.Tenants;
using Tenancy.Infrastructure.Persistence;
using Xunit;

namespace Api.IntegrationTests.Identity.Auditing;

/// <summary>
///     Boots the real API host (IIS-less, in-process TestServer) against a throw-away database on a dedicated local
///     PostgreSQL instance (Spec 08 §15.3: no Docker, no Testcontainers).
///     <para>
///         Set <c>KRAFTHAUS_TEST_POSTGRES</c> to an admin connection string for that instance, e.g.
///         <c>Host=localhost;Port=5432;Username=postgres;Password=...</c>. Each run creates
///         <c>krafthaus_it_&lt;guid&gt;</c> and drops it afterwards. Without the variable the tests are skipped.
///     </para>
/// </summary>
public sealed class IdentityAuditApiFixture : IAsyncLifetime
{
    public const string ConnectionVariable = "KRAFTHAUS_TEST_POSTGRES";

    public const string SkipReason =
        $"Set {ConnectionVariable} to an admin connection string for a dedicated local PostgreSQL test instance.";

    private static readonly string[] _connectionVariables =
    [
        "ConnectionStrings__KrafthausPlatform",
        "DatabaseOptions__ConnectionString",
        "TenancyDatabase__PlatformConnectionString",
        "TenancyDatabase__AdminConnectionString",
        "IdentityDatabase__PlatformConnectionString"
    ];

    private readonly Dictionary<string, string?> _previousEnvironment = new(StringComparer.Ordinal);
    private readonly string? _adminConnectionString = Environment.GetEnvironmentVariable(ConnectionVariable);

    private ActivityListener? _activityListener;
    private IdentityAuditApiFactory? _factory;
    private string? _databaseName;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_adminConnectionString);

    public CapturingOtpSender OtpSender { get; } = new();

    public AdjustableClock Clock { get; } = new();

    public Guid TenantId { get; private set; }

    /// <summary>A second tenant (own identity realm) reachable with <c>Host: </c><see cref="SecondTenantHost" />.</summary>
    public const string SecondTenantHost = "tenant-b.test";

    public Guid SecondTenantId { get; private set; }

    public string DatabaseConnectionString { get; private set; } = string.Empty;

    public IdentityAuditApiFactory Factory =>
        _factory ?? throw new InvalidOperationException(SkipReason);

    public async ValueTask InitializeAsync()
    {
        if (!IsConfigured)
        {
            return;
        }

        _databaseName = $"krafthaus_it_{Guid.NewGuid():N}";

        var admin = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = "postgres" };
        DatabaseConnectionString =
            new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = _databaseName }.ConnectionString;

        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
#pragma warning disable CA2100 // Database name is a generated GUID, not input.
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
            await create.ExecuteNonQueryAsync();
#pragma warning restore CA2100
        }

        // Environment variables are the last configuration source Program adds, so they win over
        // appsettings.Testing.json and are visible to registration-time reads.
        foreach (var name in _connectionVariables)
        {
            _previousEnvironment[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(
                name,
                name.EndsWith("AdminConnectionString", StringComparison.Ordinal)
                    ? admin.ConnectionString
                    : DatabaseConnectionString);
        }

        // Guarantees ASP.NET Core creates a request Activity, so audit rows carry the caller's W3C trace id.
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_activityListener);

        _factory = new IdentityAuditApiFactory(OtpSender, Clock);

        // Forces the host to start (and run module migrations) before any test executes.
        _factory.CreateClient().Dispose();

        // Each tenant gets its own throw-away tenant database, provisioned like production (Migrator): this
        // creates the database and applies every tenant-scoped module schema (Events, ...).
        TenantId = await SeedTenantAsync("AUDIT-IT", "localhost", $"{_databaseName}_t1");
        SecondTenantId = await SeedTenantAsync("AUDIT-IT-B", SecondTenantHost, $"{_databaseName}_t2");

        TenantDatabaseConnectionString = await ProvisionAsync(TenantId, $"{_databaseName}_t1");
        SecondTenantDatabaseConnectionString = await ProvisionAsync(SecondTenantId, $"{_databaseName}_t2");
    }

    /// <summary>The first (localhost) tenant's own database, where tenant-scoped modules keep their tables.</summary>
    public string TenantDatabaseConnectionString { get; private set; } = string.Empty;

    public string SecondTenantDatabaseConnectionString { get; private set; } = string.Empty;

    private async Task<string> ProvisionAsync(Guid tenantId, string databaseName)
    {
        using var scope = Factory.Services.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioner>();

        var result = await provisioner.ProvisionAsync(tenantId, CancellationToken.None);
        Assert.False(result.IsFailure, result.Error?.Description);

        return new NpgsqlConnectionStringBuilder(DatabaseConnectionString) { Database = databaseName }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        _activityListener?.Dispose();

        foreach (var (name, value) in _previousEnvironment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        if (IsConfigured && _databaseName is not null)
        {
            NpgsqlConnection.ClearAllPools();

            var admin = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = "postgres" };
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync();
#pragma warning disable CA2100 // Database names are generated GUIDs, not input.
            foreach (var name in new[] { _databaseName, $"{_databaseName}_t1", $"{_databaseName}_t2" })
            {
                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
                await drop.ExecuteNonQueryAsync();
            }
#pragma warning restore CA2100
        }
    }

    public HttpClient CreateClient() => Factory.CreateClient();

    private async Task<Guid> SeedTenantAsync(string slug, string hostName, string databaseName)
    {
        // TenancySeeder only runs in Development; resolve "localhost" to a test tenant ourselves.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();

        var tenant = Tenant.Create(
            new TenantId(Guid.CreateVersion7()),
            TenantSlug.Create(slug).Value!,
            "Audit integration tests").Value!;

        var host = TenantHost.Create(
            new TenantHostId(Guid.CreateVersion7()),
            tenant.Id,
            HostName.Create(hostName).Value!,
            HostSurface.Client | HostSurface.Admin | HostSurface.Ops | HostSurface.Display,
            isPrimary: true);

        Assert.False(tenant.AddHost(host).IsFailure);
        Assert.False(tenant.MarkProvisioned(databaseName).IsFailure);

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        return tenant.Id.Value;
    }
}

public sealed class IdentityAuditApiFactory(
    CapturingOtpSender otpSender,
    AdjustableClock clock)
    : WebApplicationFactory<Host.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // Capture OTP codes instead of sending them (and so the tests can use them).
            services.RemoveAll<IOtpSender>();
            services.AddSingleton<IOtpSender>(otpSender);

            // Lets tests step past the refresh-token grace window deterministically.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
    }
}

public sealed class CapturingOtpSender : IOtpSender
{
    private readonly Dictionary<string, string> _codes = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public Task SendAsync(
        OtpChannel channel,
        string destination,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _codes[destination] = code;
        }

        return Task.CompletedTask;
    }

    public string CodeFor(string normalizedDestination)
    {
        lock (_gate)
        {
            return _codes[normalizedDestination];
        }
    }
}

/// <summary>Real time plus an offset the tests can push forward.</summary>
public sealed class AdjustableClock : TimeProvider
{
    private readonly Lock _gate = new();
    private TimeSpan _offset = TimeSpan.Zero;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return System.GetUtcNow() + _offset;
        }
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _offset += by;
        }
    }
}
