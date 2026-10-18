using Commerce.Application.Insights;
using Common.Application.Analytics;
using Common.Application.Fees;
using Commerce.Application;
using Commerce.Application.Abstractions;
using Commerce.Application.Boards;
using Commerce.Application.Menus;
using Commerce.Application.Orders;
using Commerce.Application.Stock;
using Commerce.Application.Krafts;
using Commerce.Infrastructure.Database;
using Commerce.Infrastructure.Persistence;
using Commerce.Infrastructure.Persistence.Repositories;
using Commerce.Infrastructure.Realtime;
using Commerce.Infrastructure.Stock;
using Common.Application.Commerce;
using Common.Infrastructure.Messaging;
using Common.IntegrationEvents.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Commerce.Infrastructure;

public static class Setup
{
    public static IServiceCollection AddCommerce(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddCommercePersistence();
        services.AddModuleOutbox(CommerceDbContext.Schema);

        services.AddScoped<IKraftRepository, KraftRepository>();
        services.AddScoped<IMenuItemRepository, MenuItemRepository>();
        services.AddScoped<ICommerceUnitOfWork, CommerceUnitOfWork>();
        services.AddScoped<IStockLedger, StockLedger>();

        services.AddOptions<CommerceOptions>().BindConfiguration(CommerceOptions.SectionName).ValidateOnStart();
        services.AddPlatformFees();
        services.AddSingleton<IValidateOptions<CommerceOptions>, CommerceOptionsValidator>();

        services.AddScoped<IKraftDirectory, KraftDirectory>();
        services.AddScoped<KraftService>();
        services.AddScoped<KraftTills>();
        services.AddScoped<Commerce.Application.Images.ICommerceImageStorage, Storage.DiskCommerceImageStorage>();
        services.AddScoped<Commerce.Application.Images.CommerceImageService>();
        services.AddScoped<KraftCatalogService>();
        services.AddScoped<MenuService>();
        services.AddScoped<DisplaySignals>();
        services.AddScoped<ICommerceBoards, CommerceBoards>();
        services.AddScoped<ICommerceInsights, CommerceInsightsProvider>();

        services.AddScoped<IKraftOrderRepository, KraftOrderRepository>();
        services.AddScoped<IKraftOrderNotifier, KraftOrderNotifier>();
        services.AddScoped<IKraftOrderRealtime, SignalRKraftOrderRealtime>();
        services.AddScoped<KraftCheckoutService>();
        services.AddScoped<KitchenService>();
        services.AddScoped<KraftOrderAdminService>();
        services.AddScoped<Commerce.Application.Settlements.IKraftSettlementRepository, Persistence.Repositories.KraftSettlementRepository>();
        services.AddScoped<Commerce.Application.Settlements.SettlementService>();
        services.AddSignalR();

        services.AddIntegrationEventHandler<PaymentSucceeded, KraftOrderPaymentSucceededHandler>();
        services.AddIntegrationEventHandler<PaymentFailed, KraftOrderPaymentFailedHandler>();

        var releaseCron = configuration[$"{CommerceOptions.SectionName}:{nameof(CommerceOptions.ReleaseHoldsCron)}"] ?? "* * * * *";
        services.AddTenantJob<ReleaseExpiredKraftOrdersJob>(ReleaseExpiredKraftOrdersJob.Name, releaseCron);

        var uncollectedCron = configuration[$"{CommerceOptions.SectionName}:{nameof(CommerceOptions.UncollectedCron)}"] ?? "* * * * *";
        services.AddTenantJob<FlagUncollectedKraftOrdersJob>(FlagUncollectedKraftOrdersJob.Name, uncollectedCron);

        return services;
    }
}
