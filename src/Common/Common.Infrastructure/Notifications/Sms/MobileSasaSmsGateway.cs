using System.Net.Http.Headers;
using System.Net.Http.Json;
using Common.Application.Notifications;
using Common.Application.Options;
using Microsoft.Extensions.Options;

namespace Common.Infrastructure.Notifications.Sms;

public sealed class MobileSasaSmsGateway(
    HttpClient httpClient,
    IOptions<SmsOptions> options)
    : ISmsGateway
{
    private readonly SmsOptions _options =
        options.Value;

    public async Task SendAsync(
        SmsMessage message,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "v1/send/message");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                _options.ApiKey);

        request.Content =
            JsonContent.Create(
                new
                {
                    senderID = _options.SenderId,
                    phone = message.PhoneNumber,
                    message = message.Message
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
                $"MobileSasa SMS request failed with " +
                $"status {(int)response.StatusCode}: {body}");
        }
    }
}
