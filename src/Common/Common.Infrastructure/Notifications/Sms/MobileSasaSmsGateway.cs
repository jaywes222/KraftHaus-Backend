using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Common.Application.Notifications;
using Common.Application.Options;
using Microsoft.Extensions.Options;

namespace Common.Infrastructure.Notifications.Sms;

public sealed class MobileSasaSmsGateway(
    HttpClient httpClient,
    IOptions<SmsOptions> options)
    : ISmsGateway
{
    private static readonly string[] MessageIdNames = ["messageId", "message_id", "messageID", "MessageId", "msgId"];

    private readonly SmsOptions _options =
        options.Value;

    public async Task SendAsync(
        SmsMessage message,
        CancellationToken cancellationToken) =>
        await SendWithReceiptAsync(message, cancellationToken);

    public async Task<SmsReceipt> SendWithReceiptAsync(
        SmsMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

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

        var body =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"MobileSasa SMS request failed with " +
                $"status {(int)response.StatusCode}: {body}");
        }

        return new SmsReceipt(FindMessageId(body));
    }

    /// <summary>
    ///     The message id from MobileSasa's reply, wherever it sits (top level, <c>data</c>, or the first item of a
    ///     list). <c>null</c> when the reply is not JSON or has no id: sending still counts as accepted.
    /// </summary>
    internal static string? FindMessageId(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return Find(document.RootElement, depth: 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Find(JsonElement element, int depth)
    {
        if (depth > 3)
        {
            return null;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var name in MessageIdNames)
                {
                    if (element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    {
                        var text = value.ToString();
                        return text.Length is > 0 and <= 128 ? text : null;
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array &&
                        Find(property.Value, depth + 1) is { } nested)
                    {
                        return nested;
                    }
                }

                return null;

            case JsonValueKind.Array:
                return element.GetArrayLength() > 0 ? Find(element[0], depth + 1) : null;

            default:
                return null;
        }
    }
}
