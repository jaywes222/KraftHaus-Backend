using Common.Application.Persistence;
using Common.Application.Tenancy;
using Common.Infrastructure.Messaging;
using Common.Infrastructure.Persistence.Auditing;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Payments.Infrastructure.Auditing;
using Payments.Infrastructure.Persistence;

namespace Payments.Infrastructure.Database;

public static class PaymentsDatabaseRegistration
{
    public const string ModuleName = "Payments";

    /// <summary>
    ///     Request-time <see cref="PaymentsDbContext" /> bound to the resolved tenant's database, plus the migration
    ///     descriptor (tenant scope) so provisioning applies the Payments schema to every tenant database.
    /// </summary>
    public static IServiceCollection AddPaymentsPersistence(this IServiceCollection services)
    {
        services.AddScoped<PaymentsAuditSaveChangesInterceptor>();

        services.AddDbContext<PaymentsDbContext>((sp, options) =>
        {
            var tenantDatabase = sp.GetRequiredService<ITenantDatabaseConnection>();

            options
                .UseNpgsql(tenantDatabase.GetConnectionString(), Configure)
                .UseExceptionProcessor()
                .AddInterceptors(
                    // CreatedOn/CreatedBy/LastModifiedOn/LastModifiedBy on every auditable row.
                    sp.GetRequiredService<ApplyAuditingInterceptor>(),

                    // payments.audit_log rows in the same transaction as the change.
                    sp.GetRequiredService<PaymentsAuditSaveChangesInterceptor>());
        });

        // payments.outbox_message is drained by the tenant outbox dispatcher.
        services.AddModuleOutbox(PaymentsDbContext.Schema);

        return services.AddPaymentsMigrations();
    }

    /// <summary>
    ///     Only the migration descriptor: used by hosts that provision tenant databases without serving requests
    ///     (the Migrator).
    /// </summary>
    public static IServiceCollection AddPaymentsMigrations(this IServiceCollection services)
    {
        services.AddSingleton(
            new ModuleDatabaseDescriptor(
                ModuleName,
                DatabaseScope.Tenant,
                connectionString => new PaymentsDbContext(
                    new DbContextOptionsBuilder<PaymentsDbContext>()
                        .UseNpgsql(connectionString, Configure)
                        .Options)));

        return services;
    }

    internal static void Configure(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(PaymentsDbContext.MigrationsHistoryTable, PaymentsDbContext.Schema);
}
