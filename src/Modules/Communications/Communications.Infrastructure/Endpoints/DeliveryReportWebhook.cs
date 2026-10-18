using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Communications.Application;
using Communications.Application.Messages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Communications.Infrastructure.Endpoints;

/// <summary>
///     <c>POST /webhooks/sms/{token}</c>: the SMS provider's delivery reports (Spec 08 §9.5). The token in the path is
///     the only secret (providers do not sign reports), compared in constant time. JSON (one report or a list) and form
///     posts are both read, with the field names providers commonly use. Always 200 once the token matches, so the
///     provider does not retry reports this system has nothing to do with.
/// </summary>
internal static class DeliveryReportWebhook
{
    public const int MaxBodyBytes = 64 * 1024;
    public const int MaxReportsPerPost = 500;

    private static readonly string[] IdNames = ["messageId", "message_id", "messageID", "MessageId", "msgId", "id"];
    private static readonly string[] StatusNames = ["status", "deliveryStatus", "delivery_status", "DeliveryStatus", "dlrStatus", "state"];

    public static async Task<IResult> HandleAsync(
        string token,
        HttpRequest request,
        IOptions<CommunicationsOptions> options,
        MessageLogService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var expected = options.Value.DeliveryReportToken;
        if (string.IsNullOrEmpty(expected) || !TokenMatches(token, expected))
        {
            return Results.NotFound();
        }

        if (request.ContentLength is > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        foreach (var report in await ReadAsync(request, cancellationToken))
        {
            await service.ApplyDeliveryReportAsync(report, cancellationToken);
        }

        return Results.Ok();
    }

    internal static bool TokenMatches(string? given, string expected) =>
        given is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));

    private static async Task<IReadOnlyList<DeliveryReport>> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            return [new DeliveryReport(First(form, IdNames), First(form, StatusNames))];
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var buffer = new char[MaxBodyBytes + 1];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
        if (read > MaxBodyBytes)
        {
            return [];
        }

        return Parse(new string(buffer, 0, read));
    }

    internal static IReadOnlyList<DeliveryReport> Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            return root.ValueKind switch
            {
                JsonValueKind.Array => [.. root.EnumerateArray().Take(MaxReportsPerPost).Where(x => x.ValueKind == JsonValueKind.Object).Select(FromObject)],
                JsonValueKind.Object when root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array =>
                    [.. data.EnumerateArray().Take(MaxReportsPerPost).Where(x => x.ValueKind == JsonValueKind.Object).Select(FromObject)],
                JsonValueKind.Object => [FromObject(root)],
                _ => []
            };
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static DeliveryReport FromObject(JsonElement element) =>
        new(First(element, IdNames), First(element, StatusNames));

    private static string? First(JsonElement element, string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static string? First(IFormCollection form, string[] names)
    {
        foreach (var name in names)
        {
            if (form.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.ToString();
            }
        }

        return null;
    }
}
