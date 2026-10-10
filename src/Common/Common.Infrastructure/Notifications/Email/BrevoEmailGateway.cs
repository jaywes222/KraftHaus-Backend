using System.Net.Http.Headers;
using System.Net.Http.Json;
using Common.Application.Notifications;
using Common.Application.Options;
using Microsoft.Extensions.Options;

namespace Common.Infrastructure.Communications.Email;

public sealed class BrevoEmailGateway(
    HttpClient httpClient,
    IOptions<EmailOptions> options)
    : IEmailGateway
{
    private readonly EmailOptions _options =
        options.Value;

    public async Task SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "v3/smtp/email");

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        request.Headers.Add(
            "api-key",
            _options.ApiKey);

        request.Content =
            JsonContent.Create(
                new
                {
                    sender = new
                    {
                        name = _options.FromName,
                        email = _options.FromAddress
                    },
                    to = new[]
                    {
                        new
                        {
                            email = message.To
                        }
                    },
                    subject = message.Subject,
                    textContent = message.TextBody,
                    htmlContent = message.HtmlBody
                });

        using var response =
            await httpClient.SendAsync(
                request,
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            throw new HttpRequestException(
                $"Brevo email request failed with " +
                $"status {(int)response.StatusCode}: {body}");
        }
    }
}
