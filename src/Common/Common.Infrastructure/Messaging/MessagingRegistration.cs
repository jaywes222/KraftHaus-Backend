using Common.Application.Messaging;
using Common.Application.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Common.Infrastructure.Messaging;

public static class MessagingRegistration
{
    /// <summary>
    ///     The tenant outbox dispatcher (hosted), its signal and processor. Safe to call from several modules: it
    ///     registers once.
    /// </summary>
    public static IServiceCollection AddTenantOutboxDispatcher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(x => x.ServiceType == typeof(OutboxSignal)))
        {
            return services;
        }

        services
            .AddOptions<OutboxOptions>()
            .BindConfiguration(OutboxOptions.SectionName)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OutboxOptions>, OutboxOptionsValidator>());

        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<IOutboxSignal>(sp => sp.GetRequiredService<OutboxSignal>());
        services.AddScoped<OutboxProcessor>();
        services.AddHostedService<OutboxDispatcher>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOperationalSignal, OutboxBacklogSignal>());

        return services;
    }

    /// <summary>Declares that <paramref name="schema" /> has an <c>outbox_message</c> table to drain.</summary>
    public static IServiceCollection AddModuleOutbox(this IServiceCollection services, string schema)
    {
        services.AddTenantOutboxDispatcher();
        services.AddSingleton(new ModuleOutboxDescriptor(schema));
        return services;
    }

    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : Common.IntegrationEvents.IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        return services;
    }

    /// <summary>Registers a recurring per-tenant job; the host schedules it with Hangfire under <paramref name="name" />.</summary>
    public static IServiceCollection AddTenantJob<TJob>(this IServiceCollection services, string name, string cron)
        where TJob : class, ITenantJob
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(cron);

        services.AddScoped<TJob>();
        services.AddSingleton(new TenantJobDescriptor(name, cron, typeof(TJob)));
        return services;
    }
}
