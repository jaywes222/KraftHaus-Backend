using Common.Application.Notifications;
using Common.Application.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Infrastructure.Notifications.Sms;

public static class SmsGatewayRegistration
{
    public static IServiceCollection AddSmsGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<SmsOptions>()
            .BindConfiguration("Notifications:Sms")
            .ValidateOnStart();

        var options =
            configuration
                .GetSection("Notifications:Sms")
                .Get<SmsOptions>()
            ?? throw new InvalidOperationException(
                "Notifications:Sms configuration is required.");

        switch (options.Provider)
        {
            case SmsProvider.Dummy:

                services.AddScoped<
                    ISmsGateway,
                    DummySmsGateway>();

                break;

            case SmsProvider.MobileSasa:

                services.AddHttpClient<
                    ISmsGateway,
                    MobileSasaSmsGateway>(
                    client =>
                    {
                        client.BaseAddress =
                            new Uri(options.BaseUrl);
                    });

                break;

            case SmsProvider.AfricasTalking:

                throw new NotSupportedException(
                    "Africa's Talking SMS gateway has not been registered yet.");

            default:

                throw new InvalidOperationException(
                    $"Unsupported SMS provider '{options.Provider}'.");
        }

        return services;
    }
}
