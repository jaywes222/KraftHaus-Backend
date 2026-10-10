using Common.Application.Notifications;
using Common.Application.Options;
using Common.Infrastructure.Notifications.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Infrastructure.Communications.Email;

public static class EmailGatewayRegistration
{
    public static IServiceCollection AddEmailGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<EmailOptions>()
            .BindConfiguration("Notifications:Email")
            .ValidateOnStart();

        var options =
            configuration
                .GetSection("Notifications:Email")
                .Get<EmailOptions>()
            ?? throw new InvalidOperationException(
                "Notifications:Email configuration is required.");

        switch (options.Provider)
        {
            case EmailProvider.Dummy:

                services.AddScoped<
                    IEmailGateway,
                    DummyEmailGateway>();

                break;

            case EmailProvider.Brevo:

                services.AddHttpClient<
                    IEmailGateway,
                    BrevoEmailGateway>(
                    client =>
                    {
                        client.BaseAddress =
                            new Uri(options.BaseUrl);
                    });

                break;

            case EmailProvider.ZeptoMail:

                throw new NotSupportedException(
                    "ZeptoMail email gateway has not been registered yet.");

            default:

                throw new InvalidOperationException(
                    $"Unsupported email provider '{options.Provider}'.");
        }

        return services;
    }
}
