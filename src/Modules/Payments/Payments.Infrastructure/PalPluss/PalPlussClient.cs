using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application.Provider;
using Payments.Domain.Payments;

namespace Payments.Infrastructure.PalPluss;

/// <summary>
///     PalPluss REST client (docs.palpluss.com): <c>POST /payments/stk</c>, <c>GET /transactions/{id}</c>, HTTP Basic
///     with the API key as user name. STK requests are never retried here: a retry is a second prompt on the phone.
/// </summary>
internal sealed partial class PalPlussClient(
    HttpClient http,
    IOptions<PalPlussOptions> options,
    ILogger<PalPlussClient> logger)
    : IPaymentProvider
{
    public async Task<ProviderStartResult> StartStkAsync(ProviderStkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["amount"] = request.Amount,
            ["phone"] = request.Phone.TrimStart('+'),
            ["accountReference"] = request.AccountReference,
            ["transactionDesc"] = request.Description,
            ["callbackUrl"] = request.CallbackUrl.ToString()
        };

        // A Kraft till's own channel; otherwise the configured platform channel (or the account default).
        if ((request.ChannelId ?? options.Value.ChannelId) is { } channel)
        {
            body["channelId"] = channel.ToString("D", CultureInfo.InvariantCulture);
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "payments/stk") { Content = JsonContent.Create(body) };
        Authorize(message);

        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            var envelope = await ReadAsync(response, cancellationToken);

            if (response.IsSuccessStatusCode && envelope.Data is { } data && Str(data, "transactionId") is { } transactionId)
            {
                return new ProviderStartResult.Accepted(transactionId, Str(data, "providerCheckoutId"));
            }

            if ((int)response.StatusCode >= 500 || envelope.ErrorCode is null)
            {
                return new ProviderStartResult.Unknown($"HTTP {(int)response.StatusCode}");
            }

            LogRefused(logger, envelope.ErrorCode, envelope.RequestId);

            return new ProviderStartResult.Refused(
                envelope.ErrorCode,
                envelope.ErrorMessage,
                envelope.RetryAfterSeconds ?? RetryAfter(response));
        }
        catch (HttpRequestException ex)
        {
            return new ProviderStartResult.Unknown(ex.GetType().Name);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProviderStartResult.Unknown("timeout");
        }
        catch (JsonException)
        {
            return new ProviderStartResult.Unknown("unreadable response");
        }
    }

    public async Task<ProviderLookup> GetTransactionAsync(string transactionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        using var message = new HttpRequestMessage(HttpMethod.Get, $"transactions/{Uri.EscapeDataString(transactionId)}");
        Authorize(message);

        try
        {
            using var response = await http.SendAsync(message, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ProviderLookup.NotFound();
            }

            var envelope = await ReadAsync(response, cancellationToken);

            if (!response.IsSuccessStatusCode || envelope.Data is not { } data)
            {
                return new ProviderLookup.Unavailable(envelope.ErrorCode ?? $"HTTP {(int)response.StatusCode}");
            }

            var record = ParseRecord(data, transactionId);
            return record is null
                ? new ProviderLookup.Unavailable("unrecognised transaction record")
                : new ProviderLookup.Found(record);
        }
        catch (HttpRequestException ex)
        {
            return new ProviderLookup.Unavailable(ex.GetType().Name);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProviderLookup.Unavailable("timeout");
        }
        catch (JsonException)
        {
            return new ProviderLookup.Unavailable("unreadable response");
        }
    }

    /// <summary>
    ///     PalPluss documents camelCase on <c>GET /transactions/{id}</c> and snake_case in callbacks; both are
    ///     accepted. Unknown statuses are rejected rather than guessed.
    /// </summary>
    internal static ProviderRecord? ParseRecord(JsonElement data, string expectedTransactionId)
    {
        var id = Str(data, "transactionId", "id") ?? expectedTransactionId;
        if (!string.Equals(id, expectedTransactionId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var status = Str(data, "status")?.ToUpperInvariant() switch
        {
            "PENDING" => ProviderTransactionStatus.Pending,
            "PROCESSING" => ProviderTransactionStatus.Processing,
            "SUCCESS" => ProviderTransactionStatus.Success,
            "FAILED" => ProviderTransactionStatus.Failed,
            "CANCELLED" => ProviderTransactionStatus.Cancelled,
            "EXPIRED" => ProviderTransactionStatus.Expired,
            "REVERSED" => ProviderTransactionStatus.Reversed,
            _ => (ProviderTransactionStatus?)null
        };

        if (status is null || Decimal(data, "amount") is not { } amount)
        {
            return null;
        }

        return new ProviderRecord(
            id,
            status.Value,
            amount,
            Str(data, "accountReference", "external_reference"),
            Str(data, "mpesaReceipt", "mpesa_receipt", "receipt"),
            Str(data, "resultCode", "result_code"),
            Str(data, "resultDesc", "result_desc", "resultDescription"));
    }

    private void Authorize(HttpRequestMessage message)
    {
        var key = options.Value.ApiKey ?? string.Empty;
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(key + ":")));
    }

    private static async Task<Envelope> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Envelope(null, null, null, null, null);
        }

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        JsonElement? data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d.Clone() : null;
        string? code = null, errorMessage = null;
        int? retryAfter = null;

        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            code = Str(error, "code");
            errorMessage = Str(error, "message");

            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object &&
                details.TryGetProperty("retryAfterSeconds", out var retry) && retry.TryGetInt32(out var seconds))
            {
                retryAfter = seconds;
            }
        }

        return new Envelope(data, code, errorMessage, retryAfter, Str(root, "requestId"));
    }

    private static int? RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delta ? (int)Math.Ceiling(delta.TotalSeconds) : null;

    private static string? Str(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }

                if (value.ValueKind == JsonValueKind.Number)
                {
                    return value.GetRawText();
                }
            }
        }

        return null;
    }

    private static decimal? Decimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) switch
        {
            true when value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) => number,
            true when value.ValueKind == JsonValueKind.String &&
                      decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };

    private sealed record Envelope(JsonElement? Data, string? ErrorCode, string? ErrorMessage, int? RetryAfterSeconds, string? RequestId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PalPluss refused an STK push: {ErrorCode} (request {RequestId}).")]
    private static partial void LogRefused(ILogger logger, string errorCode, string? requestId);
}
