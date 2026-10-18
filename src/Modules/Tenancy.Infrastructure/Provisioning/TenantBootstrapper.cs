using Common.Application.Persistence;
using Common.Domain.ResultMonad;
using Common.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tenancy.Application.Abstractions;
using Tenancy.Domain.Common;
using Tenancy.Domain.Tenants;
using Tenancy.Infrastructure.Options;
using Tenancy.Infrastructure.Persistence;

namespace Tenancy.Infrastructure.Provisioning;

/// <summary>What the Migrator's <c>register-tenant</c> command asks for.</summary>
/// <param name="Hosts">Host names that resolve to this tenant (the API host first; it becomes the primary host).</param>
public sealed record TenantRegistration(
    string Slug,
    string Name,
    IdentityScope IdentityScope,
    IReadOnlyList<string> Hosts);

public sealed record TenantRegistrationResult(
    Guid TenantId,
    string DatabaseName,
    bool Created,
    IReadOnlyList<string> AddedHosts);

/// <summary>
///     Takes an empty server to a working tenant: the platform database, the tenant row, its hosts and its own
///     database with every tenant-module schema. Idempotent: running it again adds only missing hosts and applies
///     migrations added since. It never renames a tenant, removes a host or changes an identity scope.
/// </summary>
public sealed class TenantBootstrapper(
    TenancyDbContext db,
    IDatabaseInitializer databaseInitializer,
    ITenantDatabaseConnectionFactory connectionFactory,
    IOptions<TenancyDatabaseOptions> options,
    TimeProvider time)
{
    private const HostSurface AllSurfaces = HostSurface.Client | HostSurface.Admin | HostSurface.Ops | HostSurface.Display;

    public async Task<Result<TenantRegistrationResult>> RegisterAsync(
        TenantRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var slug = TenantSlug.Create(registration.Slug);
        if (slug.IsFailure)
        {
            return slug.Error!;
        }

        if (registration.Hosts.Count == 0)
        {
            return new Error { Key = "Tenant.HostRequired", Description = "Give at least one host name (--host)." };
        }

        var hostNames = new List<HostName>();
        foreach (var raw in registration.Hosts)
        {
            var host = HostName.Create(raw);
            if (host.IsFailure)
            {
                return host.Error!;
            }

            if (!hostNames.Contains(host.Value!))
            {
                hostNames.Add(host.Value!);
            }
        }

        // The platform database (and the tenancy schema) must exist before any tenant row can be written.
        await databaseInitializer.InitializePlatformAsync(cancellationToken);

        var tenant = await db.Tenants
            .Include(x => x.Hosts)
            .SingleOrDefaultAsync(x => x.Slug == slug.Value, cancellationToken);

        var created = false;
        if (tenant is null)
        {
            var newTenant = Tenant.Create(
                new TenantId(Guid.CreateVersion7(time.GetUtcNow())),
                slug.Value!,
                registration.Name,
                registration.IdentityScope);

            if (newTenant.IsFailure)
            {
                return newTenant.Error!;
            }

            tenant = newTenant.Value!;
            db.Tenants.Add(tenant);
            created = true;
        }
        else if (tenant.IdentityScope != registration.IdentityScope)
        {
            return new Error
            {
                Key = "Tenant.IdentityScopeFixed",
                Description =
                    $"Tenant '{tenant.Slug.Value}' already uses identity scope {tenant.IdentityScope}; it cannot change."
            };
        }

        var added = new List<string>();
        foreach (var hostName in hostNames.Where(h => tenant.Hosts.All(x => x.HostName != h)))
        {
            // A host may belong to one tenant only: the host name is how every request finds its tenant.
            var owner = await db.TenantHosts
                .AsNoTracking()
                .Where(x => x.HostName == hostName && x.TenantId != tenant.Id)
                .Select(x => (Guid?)x.TenantId.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (owner is not null)
            {
                return Error.ViolatesUniqueConstraint(nameof(TenantHost.HostName), hostName.Value);
            }

            var isPrimary = !tenant.Hosts.Any(x => x.IsPrimary && x.Enabled);
            var result = tenant.AddHost(TenantHost.Create(
                new TenantHostId(Guid.CreateVersion7(time.GetUtcNow())),
                tenant.Id,
                hostName,
                AllSurfaces,
                isPrimary));

            if (result.IsFailure)
            {
                return result.Error!;
            }

            added.Add(hostName.Value);
        }

        await db.SaveChangesAsync(cancellationToken);

        var databaseName = tenant.DatabaseName ?? DatabaseNameFor(tenant.Slug);

        try
        {
            await databaseInitializer.InitializeTenantAsync(
                tenant.Id.Value,
                databaseName,
                connectionFactory.Create(tenant.Id, databaseName),
                cancellationToken);
        }
        catch
        {
            if (tenant.Status != TenantStatus.Active)
            {
                tenant.MarkProvisioningFailed();
                await db.SaveChangesAsync(CancellationToken.None);
            }

            throw;
        }

        if (tenant.Status != TenantStatus.Active || tenant.DatabaseName != databaseName)
        {
            var provisioned = tenant.MarkProvisioned(databaseName);
            if (provisioned.IsFailure)
            {
                return provisioned.Error!;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return new TenantRegistrationResult(tenant.Id.Value, databaseName, created, added);
    }

    /// <summary>"KRAFTHAUS" → "krafthaus_t_krafthaus". Slugs are letters, digits and hyphens; databases take underscores.</summary>
    private string DatabaseNameFor(TenantSlug slug)
    {
#pragma warning disable CA1308 // PostgreSQL folds unquoted identifiers to lower case; database names follow suit.
        return options.Value.TenantDatabaseNamePrefix + slug.Value.ToLowerInvariant().Replace('-', '_');
#pragma warning restore CA1308
    }
}
