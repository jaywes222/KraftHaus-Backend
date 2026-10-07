# WP-03 — Tenancy

Implemented against Spec 08's database-per-tenant model.

## Delivered

- Tenant domain aggregate and invariants.
- TenantHost, PolicyVersion, TenantSlug, HostName, IdentityScope, TenantStatus and HostSurface.
- TenancyDbContext with schema-specific EF configurations.
- InfraDbContext and database identity/provisioning attempt models.
- ModuleDatabase registry with platform/tenant scopes.
- PostgreSQL database creation using an admin connection.
- Per-database PostgreSQL advisory locking.
- Database identity guard before module migration.
- Migration fan-out to registered module contexts.
- Provisioning retry (3 attempts, exponential delay) and persistent failure/success attempts.
- InitializeAll and single-tenant provisioning commands through Krafthaus.Migrator.
- Host-based tenant resolution, versioned cache keys and `tid` validation.
- Tenant data-source provider and scoped tenant context.
- EF design-time factories and PowerShell migration scripts.
- Domain, resolution, identity-guard, provisioning and architecture tests.

## Deliberate boundary

The `infra.database_identity` bootstrap table is created idempotently outside EF migrations because it is the guard that decides whether EF is allowed to operate on a database. Tenant business/module schemas remain EF-migrated.

## Runtime configuration

Set:

- `TenancyDatabase__PlatformConnectionString`
- `TenancyDatabase__AdminConnectionString`
- `TenancyDatabase__TenantDatabaseNamePrefix`

The development appsettings contains local-only PostgreSQL defaults. Production must supply real values through protected configuration/environment variables.

## Verification required on a Windows/.NET 10 machine

1. `dotnet restore`
2. `dotnet build src/dispatchtrackingV4.sln -warnaserror`
3. `dotnet test src/dispatchtrackingV4.sln -warnaserror`
4. `./scripts/ef-add.ps1 -Module Tenancy -Name VerifyModel` (inspect; do not commit a duplicate migration)
5. `./scripts/verify-db.ps1`
6. `./scripts/migrate.ps1 -Command initialize-all`
7. Provision one tenant and repeat the command to prove resume/idempotency.
8. Run two provisioning processes concurrently against the same tenant to verify advisory locking.
