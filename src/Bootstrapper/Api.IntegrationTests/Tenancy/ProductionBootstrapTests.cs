using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Common.Application.Tenancy;
using Common.Domain.Tenancy;
using Identity.Application.Options;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Tenancy.Infrastructure.Provisioning;
using Xunit;

namespace Api.IntegrationTests.Tenancy;

/// <summary>
///     Going live from an empty server: the Migrator's register-tenant (tenant, hosts, provisioned database) and the
///     one-time first Tenant Admin (<c>Identity:Bootstrap</c>), against the real host and PostgreSQL.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class ProductionBootstrapTests(IdentityAuditApiFixture fixture)
{
    [Fact]
    public async Task RegisterTenantProvisionsEverythingAndIsSafeToRunAgain()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        var slug = $"BOOT-{Guid.NewGuid():N}"[..13];
        var apiHost = $"api-{slug.ToUpperInvariant()}.test";
        var siteHost = $"site-{slug.ToUpperInvariant()}.test";
        string? databaseName = null;

        try
        {
            var first = await RegisterAsync(slug, IdentityScope.Tenant, apiHost);
            Assert.False(first.IsFailure, first.Error?.Description);
            databaseName = first.Value!.DatabaseName;
            Assert.True(first.Value.Created);
            Assert.Single(first.Value.AddedHosts);

            // The tenant database exists with the tenant-module schemas, and the API host now resolves to it.
            Assert.True(await SchemaExistsAsync(databaseName, "events"));
            Assert.True(await SchemaExistsAsync(databaseName, "ticketing"));
            using (var client = fixture.CreateClient())
            using (var events = await IdentityApi.CallAsync(client, HttpMethod.Get, "/api/client/events", host: apiHost))
            {
                Assert.Equal(HttpStatusCode.OK, events.StatusCode);
            }

            // Again, with one more host: nothing is re-created, only the host is added.
            var second = await RegisterAsync(slug, IdentityScope.Tenant, apiHost, siteHost);
            Assert.False(second.IsFailure, second.Error?.Description);
            Assert.False(second.Value!.Created);
            Assert.Equal(first.Value.TenantId, second.Value.TenantId);
            Assert.Equal(siteHost.ToUpperInvariant(), Assert.Single(second.Value.AddedHosts));

            // A host already serving another tenant is refused, and the scope of an existing tenant is fixed.
            var stolen = await RegisterAsync($"{slug}-X", IdentityScope.Tenant, "localhost");
            Assert.Equal("ViolatesUniqueConstraint", stolen.Error?.Key);

            var rescoped = await RegisterAsync(slug, IdentityScope.Platform, apiHost);
            Assert.Equal("Tenant.IdentityScopeFixed", rescoped.Error?.Key);
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task BootstrapAdminIsGrantedOnceAndCanSignIn()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        var slug = $"BOOT-{Guid.NewGuid():N}"[..13];
        var apiHost = $"api-{slug.ToUpperInvariant()}.test";
        string? databaseName = null;

        try
        {
            var registered = await RegisterAsync(slug, IdentityScope.Tenant, apiHost);
            Assert.False(registered.IsFailure, registered.Error?.Description);
            databaseName = registered.Value!.DatabaseName;
            var tenantId = registered.Value.TenantId;

            var adminPhone = IdentityApi.NewKenyanPhone();
            await SeedAsync(apiHost, adminPhone);

            // Run again with someone else configured: the tenant has an admin now, so nothing changes.
            var latePhone = IdentityApi.NewKenyanPhone();
            await SeedAsync(apiHost, latePhone);

            using (var scope = fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var admins = await db.RoleAssignments
                    .Where(x => x.Role == IdentityRole.TenantAdmin && x.ScopeId == tenantId &&
                                x.Status == RoleAssignmentStatus.Active)
                    .ToListAsync();
                Assert.Single(admins);

                var lateUser = await db.Users.AnyAsync(x => x.Phone == PhoneNumber.Create(latePhone).Value);
                Assert.False(lateUser);
            }

            // The bootstrapped admin signs in on the tenant's own host and holds Tenant Admin there.
            using var client = fixture.CreateClient();
            using var request = await IdentityApi.CallAsync(
                client,
                HttpMethod.Post,
                "/api/identity/otp/request",
                new { channel = "Phone", destination = adminPhone, purpose = "SignIn" },
                host: apiHost);
            Assert.Equal(HttpStatusCode.OK, request.StatusCode);
            var challengeId = (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetString();

            using var verify = await IdentityApi.CallAsync(
                client,
                HttpMethod.Post,
                "/api/identity/otp/verify",
                new
                {
                    challengeId,
                    code = fixture.OtpSender.CodeFor(adminPhone),
                    deviceId = IdentityApi.DeviceId,
                    displayName = "",
                    acceptTerms = false,
                    acceptPrivacy = false
                },
                host: apiHost);
            Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
            var accessToken = (await verify.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();

            using var me = await IdentityApi.CallAsync(client, HttpMethod.Get, "/api/identity/me", accessToken: accessToken, host: apiHost);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            var body = await me.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(body.GetProperty("roles").EnumerateArray(), x => x.GetProperty("role").GetString() == "TenantAdmin");
            Assert.Contains(body.GetProperty("tenantPermissions").EnumerateArray(), x => x.GetString() == "events:publish");
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    private async Task<Common.Domain.ResultMonad.Result<TenantRegistrationResult>> RegisterAsync(
        string slug,
        IdentityScope scope,
        params string[] hosts)
    {
        using var serviceScope = fixture.Factory.Services.CreateScope();
        var bootstrapper = serviceScope.ServiceProvider.GetRequiredService<TenantBootstrapper>();
        return await bootstrapper.RegisterAsync(new TenantRegistration(slug, "Bootstrap test", scope, hosts), CancellationToken.None);
    }

    private async Task SeedAsync(string host, string phone)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        var seeder = new BootstrapAdminSeeder(
            services.GetRequiredService<IdentityDbContext>(),
            Options.Create(new IdentityBootstrapOptions { AdminHost = host, AdminPhone = phone, AdminDisplayName = "Launch Admin" }),
            services.GetRequiredService<IOptions<IdentityPolicyOptions>>(),
            services.GetRequiredService<ITenantDirectory>(),
            TimeProvider.System,
            NullLogger<BootstrapAdminSeeder>.Instance);

        await seeder.SeedAsync(CancellationToken.None);
    }

    private async Task<bool> SchemaExistsAsync(string databaseName, string schema)
    {
        var builder = new NpgsqlConnectionStringBuilder(fixture.DatabaseConnectionString) { Database = databaseName };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = @schema)",
            connection);
        command.Parameters.AddWithValue("schema", schema);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task DropDatabaseAsync(string? databaseName)
    {
        if (databaseName is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        var admin = new NpgsqlConnectionStringBuilder(fixture.DatabaseConnectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
#pragma warning disable CA2100 // The name comes from TenantBootstrapper (prefix + generated slug), not input.
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
#pragma warning restore CA2100
        await drop.ExecuteNonQueryAsync();
    }
}
