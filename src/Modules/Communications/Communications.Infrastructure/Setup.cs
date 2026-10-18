using Common.Application.Notifications;
using Common.Application.Operations;
using Communications.Application.Alerts;
using Common.Infrastructure.Messaging;
using Communications.Application;
using Communications.Application.Abstractions;
using Communications.Application.Broadcasts;
using Communications.Application.Messages;
using Communications.Application.OptOuts;
using Communications.Application.Templates;
using Communications.Infrastructure.Database;
using Communications.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Communications.Infrastructure;

public static class Setup
{
    public static IServiceCollection AddCommunications(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddCommunicationsPersistence();

        services.AddOptions<CommunicationsOptions>().BindConfiguration(CommunicationsOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<CommunicationsOptions>, CommunicationsOptionsValidator>();

        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IBroadcastRepository, BroadcastRepository>();
        services.AddScoped<IOptOutRepository, OptOutRepository>();
        services.AddScoped<IMessageTemplateRepository, MessageTemplateRepository>();
        services.AddScoped<ICommunicationsUnitOfWork, CommunicationsUnitOfWork>();

        services.AddScoped<IMessageRecorder, MessageRecorder>();
        services.AddScoped<MessageLogService>();
        services.AddScoped<BroadcastService>();
        services.AddScoped<OptOutService>();
        services.AddScoped<MessageTemplateService>();

        // Operator alerts (Spec 08 §14): every module's IOperationalSignal, checked per tenant on a schedule.
        services.AddOptions<OperationalAlertOptions>().BindConfiguration(OperationalAlertOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<OperationalAlertOptions>, OperationalAlertOptionsValidator>();
        services.AddScoped<IOperationalSignal, SmsFailureSignal>();
        services.AddMemoryCache();
        var alertsCron = configuration[$"{OperationalAlertOptions.SectionName}:{nameof(OperationalAlertOptions.Cron)}"] ?? "*/5 * * * *";
        services.AddTenantJob<OperatorAlertsJob>(OperatorAlertsJob.Name, alertsCron);

        var cron = configuration[$"{CommunicationsOptions.SectionName}:{nameof(CommunicationsOptions.DispatchCron)}"] ?? "* * * * *";
        services.AddTenantJob<BroadcastDispatchJob>(BroadcastDispatchJob.Name, cron);

        return services;
    }
}
