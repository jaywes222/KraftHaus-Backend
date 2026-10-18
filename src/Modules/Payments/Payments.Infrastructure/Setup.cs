using Common.Application.Operations;
using Payments.Infrastructure.Operations;
using Common.Application.Payments;
using Common.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Common.IntegrationEvents.Payments;
using Payments.Application.Abstractions;
using Payments.Application.Destinations;
using Payments.Application.Payments;
using Payments.Application.Provider;
using Payments.Infrastructure.Database;
using Payments.Infrastructure.PalPluss;
using Payments.Infrastructure.Persistence.Repositories;

namespace Payments.Infrastructure;

public static class Setup
{
    public static IServiceCollection AddPayments(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPaymentsPersistence();

        services.AddOptions<PaymentsOptions>().BindConfiguration(PaymentsOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<PaymentsOptions>, PaymentsOptionsValidator>();
        services.AddOptions<PalPlussOptions>().BindConfiguration(PalPlussOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<PalPlussOptions>, PalPlussOptionsValidator>();

        var palPluss = configuration.GetSection(PalPlussOptions.SectionName).Get<PalPlussOptions>() ?? new PalPlussOptions();

        if (palPluss.Mode == PalPlussMode.Simulated)
        {
            services.AddScoped<IPaymentProvider, SimulatedPaymentProvider>();
        }
        else
        {
            services.AddHttpClient<IPaymentProvider, PalPlussClient>((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<PalPlussOptions>>().Value;
                client.BaseAddress = settings.ResolveBaseUrl();
                client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            });
        }

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IOperationalSignal, PaymentsSignal>();
        services.AddScoped<IPaymentsUnitOfWork, PaymentsUnitOfWork>();
        services.AddScoped<ISettlementRepository, SettlementRepository>();

        services.AddScoped<IPaymentDestinationRepository, PaymentDestinationRepository>();
        services.AddScoped<IPaymentDestinations, PaymentDestinationsReader>();
        services.AddScoped<TillService>();
        services.AddIntegrationEventHandler<PaymentSucceeded, TillTestSucceededHandler>();
        services.AddIntegrationEventHandler<PaymentFailed, TillTestFailedHandler>();

        services.AddScoped<IMobilePayments, MobilePaymentsService>();
        services.AddScoped<PaymentStatusSync>();
        services.AddScoped<PaymentCallbackService>();
        services.AddScoped<PaymentAdminService>();

        var cron = configuration[$"{PaymentsOptions.SectionName}:{nameof(PaymentsOptions.CheckPaymentsCron)}"] ?? "* * * * *";
        services.AddTenantJob<CheckPaymentsJob>(CheckPaymentsJob.Name, cron);

        return services;
    }
}
