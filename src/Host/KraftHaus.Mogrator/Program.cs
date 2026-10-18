using Common.Application.Persistence;
using Common.Domain.ResultMonad;
using Events.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Payments.Infrastructure.Database;
using Tenancy.Application.Abstractions;
using Tenancy.Infrastructure;
using Ticketing.Infrastructure.Database;
using Commerce.Infrastructure.Database;
using Common.Domain.Tenancy;
using Tenancy.Infrastructure.Provisioning;
using Displays.Infrastructure.Database;
using Music.Infrastructure.Database;
using Content.Infrastructure.Database;
using Feedback.Infrastructure.Database;
using Communications.Infrastructure.Database;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTenancy();

// Module schemas the initializer applies. Tenant-scoped modules register here so that
// initialize-all / provision create their tables in every tenant database.
builder.Services.AddSingleton<IModuleDatabaseRegistry, ModuleDatabaseRegistry>();
builder.Services.AddEventsMigrations();
builder.Services.AddTicketingMigrations();
builder.Services.AddCommerceMigrations();
builder.Services.AddDisplaysMigrations();
builder.Services.AddPaymentsMigrations();
builder.Services.AddMusicMigrations();
builder.Services.AddContentMigrations();
builder.Services.AddFeedbackMigrations();
builder.Services.AddCommunicationsMigrations();

using var host = builder.Build();

var provisioner = host.Services.GetRequiredService<ITenantProvisioner>();

var command = args.FirstOrDefault()?.Trim().ToUpperInvariant() ?? "INITIALIZE-ALL";

if (command == "REGISTER-TENANT")
{
    return await RegisterTenantAsync(host.Services, args[1..]);
}

var result = command switch
{
    "INITIALIZE-ALL" =>
        await provisioner.InitializeAllAsync(CancellationToken.None),

    "PROVISION"
        when args.Length > 1 &&
             Guid.TryParse(args[1], out var tenantId) =>
        await provisioner.ProvisionAsync(tenantId, CancellationToken.None),

    _ => Result.Failure(new Error
    {
        Key = "Migrator.InvalidCommand",
        Description = "Usage: initialize-all | provision <tenant-id> | " +
                      "register-tenant --slug <SLUG> --name <name> --host <api-host> [--host <more>] [--scope Platform|Tenant]"
    })
};

if (result.IsFailure && result.Error is { } error)
{
    await Console.Error.WriteLineAsync(
        $"{error.Key}: {error.Description}");

    return 1;
}

await Console.Out.WriteLineAsync(
    "Database initialization completed.");

return 0;

// register-tenant: empty server → platform DB, tenant row, its hosts and its provisioned database. Idempotent.
static async Task<int> RegisterTenantAsync(IServiceProvider services, string[] options)
{
    string? slug = null;
    string? name = null;
    var scope = IdentityScope.Platform;
    var hosts = new List<string>();

    if (options.Length % 2 != 0)
    {
        await Console.Error.WriteLineAsync("Migrator.InvalidCommand: every option needs a value (--flag value).");
        return 1;
    }

    foreach (var (flag, value) in options.Chunk(2).Select(pair => (pair[0], pair[1])))
    {
        switch (flag)
        {
            case "--slug":
                slug = value;
                break;
            case "--name":
                name = value;
                break;
            case "--host":
                hosts.Add(value);
                break;
            case "--scope" when Enum.TryParse<IdentityScope>(value, ignoreCase: true, out var parsed) &&
                                Enum.IsDefined(parsed):
                scope = parsed;
                break;
            default:
                await Console.Error.WriteLineAsync($"Migrator.InvalidCommand: unknown option {flag} {value}.");
                return 1;
        }
    }

    if (slug is null || name is null)
    {
        await Console.Error.WriteLineAsync("Migrator.InvalidCommand: register-tenant needs --slug and --name.");
        return 1;
    }

    using var scopeProvider = services.CreateScope();
    var bootstrapper = scopeProvider.ServiceProvider.GetRequiredService<TenantBootstrapper>();
    var registered = await bootstrapper.RegisterAsync(
        new TenantRegistration(slug, name, scope, hosts),
        CancellationToken.None);

    if (registered.IsFailure)
    {
        await Console.Error.WriteLineAsync($"{registered.Error!.Key}: {registered.Error.Description}");
        return 1;
    }

    var result = registered.Value!;
    await Console.Out.WriteLineAsync(
        $"Tenant {result.TenantId} {(result.Created ? "created" : "already existed")}; " +
        $"database {result.DatabaseName} is up to date; " +
        (result.AddedHosts.Count == 0 ? "no new hosts." : $"added hosts: {string.Join(", ", result.AddedHosts)}."));

    return 0;
}
