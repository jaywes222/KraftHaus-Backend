# Claude Code: Project Instructions

You are the Principal .NET 10 Architect for **KraftHaus**: a modular monolith on .NET 10 / C# 14 (Minimal APIs, DDD aggregates for writes, vertical slices), designed in **Spec 08 (Backend Technical Design)**. Spec 08 is the source of truth for architecture decisions (`TD-##`) and Spec 01 for flows (`UF-###`) and business rules (`BR-###`). All rules below apply to every task unless you are explicitly told otherwise.

---

## Codebase Discovery: GRAPHIFY FIRST

**HARD RULE: Always run graphify before grep, find, glob, or any other search tool. No exceptions.**

Mandatory search order:
1. Run graphify CLI directly via Bash: do NOT load the Skill for queries:
   - `graphify query "<question>"`: semantic search, broad context (BFS)
   - `graphify query "<question>" --dfs`: trace a specific path (DFS)
   - `graphify query "<question>" --budget 1500`: cap output at N tokens
   - `graphify path "<A>" "<B>"`: shortest path between two concepts
   - `graphify explain "<concept>"`: plain-language node explanation
2. `graphify-out/GRAPH_REPORT.md`: community map and god nodes
3. Direct file tools: only as a fallback when graphify yields nothing useful

---

## Hard Constraints (Spec 08 §1)

- **No Docker anywhere**: dev, CI, production. Every dependency runs as a native Windows binary or service (Windows Server 2019 VM, IIS in-process for the API, a Windows Service for the worker).
- **PostgreSQL** is the only data store. **Database per tenant** plus one platform database (TD-04).
- No Keycloak, no RabbitMQ / MassTransit for new work, no Redis (TD-05/06/07). See "Legacy boilerplate" below.
- Event 001 is 7 Nov 2026: build P0 flows only; prefer the simplest design that can be operated alone.

---

## Architecture

### Solution layout (`src/krafthaus.sln`)

| Path | Responsibility |
| :--- | :--- |
| `src/Bootstrapper/Api` | API host and composition root. Loads module assemblies, registers enabled modules (`ModulesOptions.EnabledModules`, `"*"` = all), middleware order, health checks. |
| `src/Bootstrapper/Api.IntegrationTests` | HTTP tests against the real host + a local PostgreSQL (`KRAFTHAUS_TEST_POSTGRES`). |
| `src/Host/KraftHaus.Mogrator` | The **Migrator** CLI (`KraftHaus.Migrator.csproj`; the folder name has a typo): `initialize-all` / `provision <tenant-id>`. (`src/Host/KraftHaus.Migrator` is an unused stub project.) |
| `src/Common/*` | Shared kernel only (Result, Money, strongly typed ids, audit contract, tenancy abstractions, auth helpers). **Zero business logic.** |
| `src/Modules/<Module>/<Module>.{Domain,Application,Infrastructure,Tests}` | One module. Tenancy's Infrastructure project sits at `src/Modules/Tenancy.Infrastructure`. |
| `scripts/*.ps1` | PowerShell build/migrate helpers (no Make). |
| `docs/` | Work-package notes, manual testing guides, Postman collections. |

### Modules

| Module | Database | Status | Notes |
| :--- | :--- | :--- | :--- |
| Tenancy | platform (`tenancy`, `infra` schemas) | done (WP-03) | Tenant registry, host → tenant resolution middleware, provisioning (create DB + apply every tenant-scoped module schema, advisory lock, retries), dev seeder (`DEV` tenant on `localhost`, DB `krafthaus_dev`). |
| Identity | platform (`identity`) | done | OTP sign-in (Duende IdentityServer issues short-lived JWTs + rotating refresh tokens), sessions, staff invitations, role assignments, permission catalog + matrix, security audit log (WP-08). |
| Events | **tenant** (`events`) | done | Venue library; events with venue copy, entrances, dates → per-date tiers → sale phases + Couple/Gang group offers; policies; lifecycle; public client view; audit log. See `docs/EVENTS-MODULE.md`. |
| Ticketing | **tenant** (`ticketing`) | S1-S4 done | Waitlist (UF-001) with SMS resends; orders with capacity holds (`capacity_bucket`/`capacity_hold`, atomic SQL), checkout with `Idempotency-Key`, early access, tickets issued on `PaymentSucceeded`, hold-expiry job, late payments; check-in, transfers, attendee names, Lipa Mdogo Mdogo; Finance resolve/void. See `docs/TICKETING-MODULE.md`. |
| Payments | **tenant** (`payments`) | PalPluss STK, tills done | M-Pesa through **PalPluss** (not Daraja): STK push, per-payment callback token, outcome always read from `GET /transactions/{id}`, status-check job, Finance list, review, settlement (BR-025); Kraft tills = PalPluss channels with test and approval (UF-017). See `docs/PAYMENTS-MODULE.md`. |
| Commerce | **tenant** (`commerce`) | S5 P0 done | Krafts, brands, Krafters, event terms with commission %; menus with stock buckets/holds; split-cart checkout (one order and prompt per Kraft, D-03), ticket gate per date (O-04), reservations; kitchen board and pickup (SignalR hubs), O-05 flags, Finance sales. See `docs/COMMERCE-MODULE.md`. |
| Displays | **tenant** (`displays`) | UF-019 done | Screens paired by code, device tokens (`DisplayDevice` auth scheme, revocable), configurations (slide playlists), time schedules, programme, images on disk (`Displays:AssetsRootPath`), feed resolving menu/ready orders via `ICommerceBoards`, hub `/hubs/display`. See `docs/DISPLAYS-MODULE.md`. |
| Music | **tenant** (`music`) | song requests done | Request board per event (open/closed, per-person limit, note), MusicBrainz search (one call a second, cached) or typed songs, votes, DJ console (up next/played/pass/undo), DJ role (`music:requests`). See `docs/MUSIC-MODULE.md`. |
| Communications | **tenant** (`communications`) | UF-020 done | Message log for every SMS/e-mail (gateway decorator `AddMessageRecording`, codes masked), SMS delivery-report webhook, broadcasts to event audiences (waitlist marketing / waitlist without ticket / ticket holders) through a throttled dispatcher job, templates, marketing opt-outs, operator alerts job over every `IOperationalSignal`. See `docs/COMMUNICATIONS-MODULE.md`. |
| Feedback | **tenant** (`feedback`) | UF-010 done | Anonymous ratings per event date by checked-in guests (`IAttendance`), one per guest per date via an HMAC dedupe hash (BR-027); no audit log and no auditing interceptors on purpose. Requires `Feedback:DedupeSecret` in Production. See `docs/FEEDBACK-MODULE.md`. |
| Content | **tenant** (`content`) | UF-024 done | Articles (Markdown, tags, covers on disk under `Content:ImagesRootPath`) and Web Studio blocks (draft/publish JSON). See `docs/CONTENT-MODULE.md`. |
| Analytics | none | UF-023 done | Event dashboard and public leaderboard composed from `ITicketingInsights`, `ICommerceInsights`, `IMusicInsights`, `IFeedbackInsights`, cached briefly. See `docs/ANALYTICS-MODULE.md`. |

### Boundaries

- A module's projects reference **only** `src/Common/*` and their own module. Never another module (architecture tests in each `<Module>.Tests` enforce it).
- Cross-module needs go through abstractions in `Common.Application` implemented by the owning module (e.g. `ICurrentTenant`, `ITenantDirectory`, `ITenantDatabaseConnection` are implemented by Tenancy). Async: integration events declared in `Common.IntegrationEvents`, written to the producing module's `<schema>.outbox_message` in the same `SaveChanges` (`db.AddIntegrationEvent`), drained per tenant by `OutboxDispatcher` and handled by `IIntegrationEventHandler<T>` (idempotent via the consumer's `processed_message`). Sync: interfaces in `Common.Application` implemented by the owning module (`IEventCatalog`, `IMobilePayments`, `ICustomerDirectory`, `IPaymentDestinations`, `ITicketHolders`, `IKraftDirectory`, `ICommerceBoards`, `IDisplayBoardSignal`). Music uses `IEventCatalog`, `ICustomerDirectory` and `ITicketHolders`. Communications uses `IAudienceDirectory` (Ticketing); Feedback uses `IAttendance` (Ticketing); Analytics uses the `*Insights` interfaces in `Common.Application.Analytics`. Operator alerts: a module that can fail in a way users feel registers an `IOperationalSignal` (`Common.Application.Operations`); counts only, no personal data.
- Each module owns its schema; no cross-module joins.
- Recurring per-tenant work implements `ITenantJob` and is registered with `AddTenantJob<T>(name, cron)`; Hangfire (PostgreSQL, `hangfire` schema in the platform DB) runs it once per active tenant via `ITenantScopeRunner`.

### Tenancy and databases (Spec 08 §5)

- The **host name decides the tenant** (`TenantResolutionMiddleware`). A token's `tid` must match it. Nothing in a request body names a tenant.
- **Platform-scoped** modules (Tenancy, Identity) use the platform connection string and migrate at start-up (`app.UseMigration<TContext>()`).
- **Tenant-scoped** modules (Events, Ticketing, Payments, Commerce, Displays, Music, Communications, Feedback, Content):
  - Build their `DbContext` from `ITenantDatabaseConnection.GetConnectionString()` (the resolved tenant's DB).
  - Register a `ModuleDatabaseDescriptor(name, DatabaseScope.Tenant, ...)`, and add the same registration to the Migrator, so provisioning applies the schema to every tenant DB.
  - Never migrate at API start-up. The Migrator (`./scripts/migrate.ps1`) and the Development seeder (re-runs the idempotent initializer on every start) apply migrations.
  - Design-time factory reads `KRAFTHAUS_TENANT_CONNECTION` (or `--connection=`).

### Authentication and authorization (Spec 08 §6)

- Everyone signs in with OTP. Access tokens are validated by Duende's local API scheme plus `ActiveSessionTokenValidator` (revoked sessions stop working). Claims: `sub`, `sid`, `tid`, `identity_scope`.
- Protect endpoints with `.RequirePermission("<area>:<action>")` from `Common.Application.Auth`. Permission strings are listed in `IdentityPermission` and mapped to roles in `IdentityPermissionMatrix` (the Spec 01 §3 matrix as code). A module keeps its own constants (e.g. `EventsPermission`) and a test keeps them in step with Identity's catalog.
- Scope is resolved by `PermissionAuthorizationHandler`: tenant from the host; a `kraftId` route value means Kraft scope (BR-026).
- Customer endpoints filter by the authenticated customer; there is no endpoint that accepts a customer id from the client.

### Auditing (Spec 08 §13)

- Each module projects its domain events into its own append-only audit table **in the same `SaveChanges`** via a `SaveChangesInterceptor` (Identity: `identity.security_audit_log`, Events: `events.audit_log`). The audit row id is the domain event id.
- Rows use the shared `AuditEntry` contract (`Common.Application.Auditing`), with actor, tenant and trace from `IAuditRequestContextProvider`. Metadata always goes through `AuditMetadataSanitizer`: no OTPs, tokens, phone numbers, e-mails or names.
- A database trigger rejects `UPDATE`/`DELETE`/`TRUNCATE`; a retention purge opts in with `SET LOCAL krafthaus.audit_purge = 'on'`.
- `ApplyAuditingInterceptor` stamps `CreatedOn/By`, `LastModifiedOn/By`. Never set them by hand.

### Legacy boilerplate (do not build on it)

The repo still contains pieces of the original modular-monolith boilerplate: MassTransit/RabbitMQ setup in the API, `BaseDbContext`/`OutboxSaveHelper` (writes to an `"Outbox"."OutboxMessages"` table no KraftHaus module has), Keycloak-named types in `Common.Application.Auth` (`KeycloakScopes`, `RequireScope`), Redis/FusionCache packages and `Common.InterModuleRequests/IAM|Notifications` contracts. KraftHaus modules do **not** use them. New code follows Spec 08 instead: PostgreSQL outbox, `HybridCache`, OTP auth and `RequirePermission`. Removing the leftovers is a separate clean-up task. Flag it, don't do it as a side effect.

---

## Module Code Shape (follow Identity / Events)

```
<Module>.Domain/          Aggregates (AggregateRoot<TId>), child entities (AuditableEntity<TId>), owned values,
                          strongly typed ids (readonly record struct XxxId(Guid Value) : IStronglyTypedId),
                          <Module>Errors (keys "<Module>.<Reason>"), domain events (sealed records : DomainEvent),
                          permission constants.
<Module>.Application/     Use-case services returning Result / Result<T>, repository + unit-of-work interfaces,
                          request inputs and response views (no AutoMapper; map in code).
<Module>.Infrastructure/  DbContext (own schema), EF configurations, repositories, migrations, audit interceptor,
                          endpoints (Minimal APIs), Setup.cs (DI), <Module>Module : IModule.
<Module>.Tests/           Domain/application unit tests + architecture tests (xUnit v3, no underscores in names).
```

- **Aggregates** validate in their command methods and return `Result`. A command builds its domain event first, then mutates, then calls `RaiseEvent(@event)`. Every raised event increments the aggregate `Version`, which is the optimistic concurrency token (`aggregate_version` column). Aggregates are not event-sourced.
- **Endpoints** are thin: bind the route and body, call one service method, then `.AddEndpointFilter<ResultToResponseTransformer<T>>()` (200), `ResultToCreatedResponseTransformer<T>` (201) or `ResultToResponseTransformer` (204).
- **Errors** carry the HTTP status: 400 for invalid input, 404 for not found, 409 for a state conflict. Clients branch on `Error.Key`.
- **EF Core**: tables snake_case per module schema, columns PascalCase; client-generated GUID v7 keys (`ValueGeneratedNever`); enums stored as strings; `DateTimeOffset` always UTC (`UtcDateTimeOffsetConverter`); money as `numeric(18,2)` + currency code.
- **Reads** use `AsNoTracking()`. Hot or complex reads (dashboards, boards) may use Dapper/raw SQL projections (Spec 08 §12).
- **Time** is read through `TimeProvider`, never `DateTime.Now`. Domain methods take `now` as a parameter.
- **IDs** are `Guid.CreateVersion7(time.GetUtcNow())` wrapped in strongly typed ids.

---

## Coding Rules (Violations = Fail)

### C# 14 standards

- **Zero warnings**: `TreatWarningsAsErrors`, `AnalysisMode=all`, Sonar analyzers. Nullability enabled and enforced.
- Primary constructors; `required` / positional records for DTOs.
- **Using directives, never full qualifiers** (a qualifier is acceptable only to resolve a genuine ambiguity at the call site). Beware: inside `Events.*` namespaces a bare `Common.` resolves to `Events.Domain.Common`, so import with `using` at the top of the file.
- **Logging**: `LoggerMessage` source generation only. No interpolated log strings. Never log secrets, OTPs, tokens or personal data.
- **Result wrapping**: never `(Result<T>)value` explicit casts. Rely on the implicit `T → Result<T>` and `Error → Result<T>` operators, or use `Result<T>.Success(value)`.
- **Typed parameters over raw strings**: enums, bools and strongly typed ids at the API edge. No branching on magic strings.
- **Money**: `Common.Domain.Currency.Money` (KES, decimal). No floating point anywhere.

### Tunable values

Never hardcode an operator-tunable value (timeouts, retries, thresholds, durations, limits) in application code. Use an options class bound from configuration and validated at start-up (`ValidateOnStart`), as `IdentityPolicyOptions` and `OtpSecurityOptions` do. Domain invariants that are product rules (e.g. a Couple offer admits 2) are constants on the domain type, not options.

### Zero trust

- Validate every input before it reaches the domain or persistence (domain factories return `Result`; FluentValidation where a request needs it).
- Assume third-party APIs fail, time out or return garbage. Use resilience handlers, and never let a provider failure cascade.
- Never call `services.BuildServiceProvider()` inside DI registration code.
- Secrets live in user-secrets (development) or environment variables / protected config (production). Never in committed appsettings.

---

## Testing (Spec 08 §15.3, no Docker)

- **xUnit v3**, `Assert.*` only (no FluentAssertions); NSubstitute/Moq only for external APIs.
- **Unit tests** per module for domain rules and services. **Architecture tests** per module for project references (and tenant scope for tenant-scoped stores).
- **Integration tests** (`Api.IntegrationTests`) boot the real host with `WebApplicationFactory` against a **local PostgreSQL** given by `KRAFTHAUS_TEST_POSTGRES` (admin connection string). Without it they skip.
  - The shared fixture creates a throw-away platform DB, seeds two tenants (`localhost`, `tenant-b.test`) and provisions both tenant DBs through `ITenantProvisioner`. It drops all three afterwards.
  - All HTTP test classes share one collection (`IdentityApiCollection`), so only one host boots at a time (shared static logger / ActivitySource).
  - For writes, assert the database state and the audit row.
- **Timestamps read back from PostgreSQL**: compare with a tolerance (`Assert.Equal(expected, actual, TimeSpan.FromSeconds(1))`). `timestamptz` is microsecond precision.
- Bug fixes: write the failing test first (red), then fix (green).

---

## Commands (PowerShell)

```powershell
dotnet build src/krafthaus.sln            # warnings are errors
dotnet test  src/krafthaus.sln            # set KRAFTHAUS_TEST_POSTGRES for the HTTP tests
dotnet run --project src/Bootstrapper/Api # Development: seeds the DEV tenant (localhost) and a Tenant Admin

./scripts/ef-add.ps1 -Module <M> -Name <Migration> -Context <M>DbContext `
    -Project src/Modules/<M>/<M>.Infrastructure/<M>.Infrastructure.csproj
./scripts/migrate.ps1                      # initialize-all: platform + every tenant DB
./scripts/migrate.ps1 -Command provision -TenantId <guid>
./scripts/verify-db.ps1
./scripts/build.ps1 | ./scripts/test.ps1 -Postgres "<admin connection>"
./scripts/publish.ps1                      # out/<stamp>/{api,migrator} for the server
./scripts/deploy.ps1 -ReleaseSource <folder>   # on the server: backup, migrate, swap, health check (-Rollback <stamp>)
./scripts/backup.ps1 | ./scripts/restore.ps1 -BackupFolder <folder> | ./scripts/install-backup-task.ps1
```

Note: `scripts/` and `appsettings*.json` are git-ignored (`.gitignore` lines `scripts/` and `appsettings*.json`), so these files are not in source control.

Manual testing: `docs/IDENTITY-MANUAL-TESTING.md` and the Postman collections in `docs/postman/`. OTP codes are printed in the API console in Development (`DEVELOPMENT ONLY: OTP ...`). The Hangfire dashboard is at `/hangfire` in Development only (local requests).

---

## Proactive Issue Reporting: Never Silently Pass Over a Bug

If you spot a bug, architecture violation, security hole, dead code, or footgun while working (even in a file you didn't touch, and even if it's unrelated to the current task), **flag it explicitly to the user** before finishing your response. `file:line` + one-line description + severity is enough.

- Never silently fix it (scope creep) and never silently ignore it.
- Never write "this was already broken / pre-existing / unrelated to my change" as a reason to drop it.
- Flag it even when it blocks nothing and the current task succeeds anyway.
