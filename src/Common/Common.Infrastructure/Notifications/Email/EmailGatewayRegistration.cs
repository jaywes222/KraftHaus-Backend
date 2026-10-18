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

                services.AddHttpClient<
                    IEmailGateway,
                    ZeptoMailEmailGateway>(
                    client =>
                    {
                        client.BaseAddress =
                            new Uri(options.BaseUrl);

                        // An OTP request waits for this call; never the HttpClient default of 100 seconds.
                        client.Timeout =
                            TimeSpan.FromSeconds(options.TotalRequestTimeoutSeconds);
                    });

                break;

            default:

                throw new InvalidOperationException(
                    $"Unsupported email provider '{options.Provider}'.");
        }

        return services;
    }
}
