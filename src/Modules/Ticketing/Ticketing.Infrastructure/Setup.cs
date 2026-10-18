using Common.Application.Operations;
using Common.Application.Analytics;
using Common.Application.Communications;
using Common.Application.Ticketing;
using Common.Application.Fees;
using Common.Infrastructure.Messaging;
using Common.IntegrationEvents.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions;
using Ticketing.Application.CheckIn;
using Ticketing.Application.Checkout;
using Ticketing.Application.Orders;
using Ticketing.Application.Tickets;
using Ticketing.Application.Waitlist;
using Ticketing.Infrastructure.Checkout;
using Ticketing.Infrastructure.Database;
using Ticketing.Infrastructure.Persistence;
using Ticketing.Infrastructure.Persistence.Repositories;
using Ticketing.Infrastructure.ReadModels;

namespace Ticketing.Infrastructure;

public static class Setup
{
    public static IServiceCollection AddTicketing(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddTicketingPersistence();
        services.AddModuleOutbox(TicketingDbContext.Schema);

        services.AddOptions<WaitlistOptions>().BindConfiguration(WaitlistOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<WaitlistOptions>, WaitlistOptionsValidator>();
        services.AddOptions<OrderOptions>().BindConfiguration(OrderOptions.SectionName).ValidateOnStart();
        services.AddOptions<LipaMdogoMdogoOptions>().BindConfiguration(LipaMdogoMdogoOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<LipaMdogoMdogoOptions>, LipaMdogoMdogoOptionsValidator>();
        services.AddSingleton<IValidateOptions<OrderOptions>, OrderOptionsValidator>();
        services.AddPlatformFees();

        services.AddOptions<TicketQrOptions>().BindConfiguration(TicketQrOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<TicketQrOptions>, TicketQrOptionsValidator>();
        services.AddSingleton<IPostConfigureOptions<TicketQrOptions>, DevelopmentQrKey>();
        services.AddSingleton<TicketQrCodec>();

        services.AddScoped<IWaitlistRepository, WaitlistRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<ITicketingUnitOfWork, TicketingUnitOfWork>();
        services.AddScoped<ICapacityLedger, CapacityLedger>();

        services.AddScoped<IWaitlistConfirmationSender, WaitlistConfirmationSender>();
        services.AddScoped<WaitlistService>();

        services.AddScoped<ITicketNotifier, TicketNotifier>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<MyTicketsService>();
        services.AddScoped<OrderAdminService>();
        services.AddScoped<CheckInService>();
        services.AddScoped<TicketHolderService>();
        services.AddScoped<Common.Application.Ticketing.ITicketHolders, TicketHolders>();
        services.AddScoped<Common.Application.Ticketing.IWaitlistMembership, WaitlistMembership>();
        services.AddScoped<FeeQuoteService>();

        // Read-only views for Communications (audiences), Feedback (attendance) and Analytics (insights).
        services.AddScoped<IAudienceDirectory, AudienceDirectory>();
        services.AddScoped<IAttendance, Attendance>();
        services.AddScoped<ITicketingInsights, TicketingInsightsProvider>();
        services.AddScoped<IOperationalSignal, HoldBacklogSignal>();

        services.AddIntegrationEventHandler<PaymentSucceeded, OrderPaymentSucceededHandler>();
        services.AddIntegrationEventHandler<PaymentFailed, OrderPaymentFailedHandler>();

        var cron = configuration[$"{OrderOptions.SectionName}:{nameof(OrderOptions.ReleaseHoldsCron)}"] ?? "* * * * *";
        services.AddTenantJob<ReleaseExpiredHoldsJob>(ReleaseExpiredHoldsJob.Name, cron);

        var resendCron = configuration[$"{WaitlistOptions.SectionName}:{nameof(WaitlistOptions.ResendCron)}"] ?? "*/15 * * * *";
        services.AddTenantJob<WaitlistResendJob>(WaitlistResendJob.Name, resendCron);

        var installmentsCron = configuration[$"{LipaMdogoMdogoOptions.SectionName}:{nameof(LipaMdogoMdogoOptions.RemindersCron)}"] ?? "7 * * * *";
        services.AddTenantJob<BalanceRemindersJob>(BalanceRemindersJob.Name, installmentsCron);

        return services;
    }

    /// <summary>Development and the Testing environment get the public development key when none is configured.</summary>
    private sealed class DevelopmentQrKey(IHostEnvironment environment) : IPostConfigureOptions<TicketQrOptions>
    {
        public void PostConfigure(string? name, TicketQrOptions options)
        {
            if (options.Keys.Count == 0 && (environment.IsDevelopment() || environment.IsEnvironment("Testing")))
            {
                options.Keys["dev"] = TicketQrOptions.DevelopmentKey;
            }

            if (string.IsNullOrEmpty(options.ActiveKeyId) && options.Keys.Count == 1)
            {
                options.ActiveKeyId = options.Keys.Keys.Single();
            }
        }
    }
}
