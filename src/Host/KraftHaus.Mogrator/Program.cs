using Common.Domain.ResultMonad;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tenancy.Application.Abstractions;
using Tenancy.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTenancy();

using var host = builder.Build();

var provisioner = host.Services.GetRequiredService<ITenantProvisioner>();

var command = args.FirstOrDefault()?.Trim().ToUpperInvariant() ?? "INITIALIZE-ALL";

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
        Description = "Usage: initialize-all | provision <tenant-id>"
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
