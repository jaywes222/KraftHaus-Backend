using Common.Application.Options;
using Common.Application.Validation;
using FluentValidation;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Common.Tests;

/// <summary>
///     Production refuses Dummy providers and a single SignalR instance, unless the deployment says explicitly that
///     the feature is unused (Enabled = false) or runs as one instance (Spec 08: one VM, no Redis).
/// </summary>
public sealed class ProductionOptOutTests
{
    [Fact]
    public void DummyCaptchaFailsInProductionWhileEnabled()
    {
        Assert.False(ValidateInProduction(new CaptchaOptionsValidator(), Captcha(enabled: true)));
        Assert.True(ValidateInProduction(new CaptchaOptionsValidator(), Captcha(enabled: false)));
    }

    [Fact]
    public void DummyPushFailsInProductionWhileEnabled()
    {
        Assert.False(ValidateInProduction(new PushOptionsValidator(), Push(enabled: true)));
        Assert.True(ValidateInProduction(new PushOptionsValidator(), Push(enabled: false)));
    }

    [Fact]
    public void DummyWarehouseFailsInProductionWhileEnabled()
    {
        Assert.False(ValidateInProduction(new InventoryOptionsValidator(), Inventory(enabled: true)));
        Assert.True(ValidateInProduction(new InventoryOptionsValidator(), Inventory(enabled: false)));
    }

    [Fact]
    public void SignalRWithoutBackplaneNeedsTheSingleInstanceOptOutInProduction()
    {
        var validator = new SignalROptionsValidator();
        Assert.False(ValidateInProduction(validator, new SignalROptions { RedisConnectionString = "" }));
        Assert.True(ValidateInProduction(
            validator,
            new SignalROptions { RedisConnectionString = "", AllowSingleInstanceInProduction = true }));
    }

    private static bool ValidateInProduction<T>(IValidator<T> validator, T options)
    {
        var context = new ValidationContext<T>(options);
        context.RootContextData[ValidationContextExtensions.HostEnvironmentKey] = new ProductionEnvironment();
        return validator.Validate(context).IsValid;
    }

    private static CaptchaOptions Captcha(bool enabled) => new()
    {
        Enabled = enabled,
        BaseUrl = "https://localhost",
        CaptchaEndpoint = "/captcha",
        ClientKey = "client",
        SecretKey = "secret",
        Provider = CaptchaProvider.Dummy,
        AttemptTimeoutSeconds = 5,
        TotalRequestTimeoutSeconds = 15
    };

    private static PushOptions Push(bool enabled)
    {
        var options = new PushOptions { Enabled = enabled, Provider = PushProvider.Dummy, Templates = new PushTemplatesOptions() };
        options.Templates.SecurityAlert["en"] = new PushTemplate("Security alert", "Something happened.");
        return options;
    }

    private static InventoryOptions Inventory(bool enabled) => new()
    {
        Enabled = enabled,
        MaxOccurrencesPerSeries = 100,
        SafetyBufferQuantity = 5,
        StockReservationExpirySweepCron = "*/5 * * * *",
        StockReservationExpirySweepBatchSize = 100,
        WarehouseGatewayProvider = WarehouseGatewayProvider.Dummy,
        WebhookSharedSecret = "secret"
    };

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
