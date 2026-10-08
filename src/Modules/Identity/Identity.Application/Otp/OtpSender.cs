using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Common.Application.Options;
using Identity.Application.Abstractions;
using Identity.Domain.Otp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Application.Otp;

public sealed class OtpSender(
    HttpClient httpClient,
    IOptions<SmsOptions> options,
    ILogger<OtpSender> logger)
    : IOtpSender
{
    private readonly SmsOptions _options = options.Value;

    public async Task SendAsync(
        OtpChannel channel,
        string destination,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        cancellationToken.ThrowIfCancellationRequested();

        if (channel != OtpChannel.Phone)
        {
            throw new NotSupportedException(
                $"OTP channel '{channel}' is not supported by the SMS sender.");
        }

        var template = ResolveTemplate();

        var message = string.Format(
            CultureInfo.InvariantCulture,
            template,
            code);

        switch (_options.Provider)
        {
            case SmsProvider.Dummy:
                LogDummyDelivery(
                    destination,
                    code,
                    purpose);

                return;

            case SmsProvider.NetGsm:
                await SendViaNetGsmAsync(
                    destination,
                    message,
                    purpose,
                    cancellationToken);

                return;

            default:
                throw new InvalidOperationException(
                    $"Unsupported SMS provider '{_options.Provider}'.");
        }
    }

    private string ResolveTemplate()
    {
        if (_options.Templates.Otp.TryGetValue(
                "en",
                out var template))
        {
            return template;
        }

        var fallback = _options.Templates.Otp
            .Values
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(fallback))
        {
            throw new InvalidOperationException(
                "No OTP SMS template is configured.");
        }

        return fallback;
    }

    private void LogDummyDelivery(
        string destination,
        string code,
        OtpPurpose purpose)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var maskedDestination = MaskDestination(destination);

        logger.LogInformation(
            "Dummy OTP delivery completed. " +
            "Purpose: {Purpose}, Destination: {Destination}, Code: {Code}",
            purpose,
            maskedDestination,
            code);
    }

    private async Task SendViaNetGsmAsync(
        string destination,
        string message,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                _options.UserCode))
        {
            throw new InvalidOperationException(
                "SmsOptions.UserCode is required for NetGsm.");
        }

        if (string.IsNullOrWhiteSpace(
                _options.Password))
        {
            throw new InvalidOperationException(
                "SmsOptions.Password is required for NetGsm.");
        }

        if (string.IsNullOrWhiteSpace(
                _options.MsgHeader))
        {
            throw new InvalidOperationException(
                "SmsOptions.MsgHeader is required for NetGsm.");
        }

        var request = new NetGsmOtpRequest
        {
            MsgHeader = _options.MsgHeader,
            Msg = message,
            Number = NormalizePhoneNumber(destination)
        };

        if (!string.IsNullOrWhiteSpace(
                _options.AppName))
        {
            request.AppName = _options.AppName;
        }

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/sms/rest/v2/otp");

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                $"{_options.UserCode}:{_options.Password}"));

        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Basic",
                credentials);

        httpRequest.Content =
            JsonContent.Create(request);

        var response = await httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogNetGsmDeliveryFailed(
                response.StatusCode,
                destination,
                purpose);

            throw new HttpRequestException(
                $"NetGsm OTP delivery failed with HTTP status {(int)response.StatusCode}.");
        }

        NetGsmResponse? result;

        try
        {
            result =
                System.Text.Json.JsonSerializer.Deserialize<NetGsmResponse>(
                    responseBody);
        }
        catch (Exception exception)
        {
            LogNetGsmInvalidResponse(
                exception,
                destination,
                purpose);

            throw new InvalidOperationException(
                "NetGsm returned an invalid response.",
                exception);
        }

        if (result is null ||
            !string.Equals(
                result.Code,
                "00",
                StringComparison.OrdinalIgnoreCase))
        {
            LogNetGsmRejected(
                result?.Code,
                destination,
                purpose);

            throw new InvalidOperationException(
                "NetGsm rejected the OTP delivery request.");
        }

        LogNetGsmDeliveryAccepted(
            destination,
            purpose);
    }

    private void LogNetGsmDeliveryFailed(
        System.Net.HttpStatusCode statusCode,
        string destination,
        OtpPurpose purpose)
    {
        if (!logger.IsEnabled(LogLevel.Error))
        {
            return;
        }

        var maskedDestination = MaskDestination(destination);

        logger.LogError(
            "NetGsm OTP delivery failed with HTTP status {StatusCode}. " +
            "Purpose: {Purpose}, Destination: {Destination}",
            (int)statusCode,
            purpose,
            maskedDestination);
    }

    private void LogNetGsmInvalidResponse(
        Exception exception,
        string destination,
        OtpPurpose purpose)
    {
        if (!logger.IsEnabled(LogLevel.Error))
        {
            return;
        }

        var maskedDestination = MaskDestination(destination);

        logger.LogError(
            exception,
            "NetGsm returned an invalid OTP response. " +
            "Purpose: {Purpose}, Destination: {Destination}",
            purpose,
            maskedDestination);
    }

    private void LogNetGsmRejected(
        string? providerCode,
        string destination,
        OtpPurpose purpose)
    {
        if (!logger.IsEnabled(LogLevel.Error))
        {
            return;
        }

        var maskedDestination = MaskDestination(destination);

        logger.LogError(
            "NetGsm rejected OTP delivery. " +
            "ProviderCode: {ProviderCode}, Purpose: {Purpose}, Destination: {Destination}",
            providerCode,
            purpose,
            maskedDestination);
    }

    private void LogNetGsmDeliveryAccepted(
        string destination,
        OtpPurpose purpose)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var maskedDestination = MaskDestination(destination);

        logger.LogInformation(
            "OTP delivery accepted by NetGsm. " +
            "Purpose: {Purpose}, Destination: {Destination}",
            purpose,
            maskedDestination);
    }

    private static string NormalizePhoneNumber(
        string destination)
    {
        var value = destination.Trim();

        if (value.StartsWith(
                '+'))
        {
            value = value[1..];
        }

        return value;
    }

    private static string MaskDestination(
        string destination)
    {
        return destination.Length <= 4
            ? "***"
            : $"{destination[..Math.Min(3, destination.Length)]}" +
            "***" +
            destination[^2..];
    }

    private sealed class NetGsmOtpRequest
    {
        [JsonPropertyName("msgheader")]
        public required string MsgHeader { get; init; }

        [JsonPropertyName("msg")]
        public required string Msg { get; init; }

        [JsonPropertyName("no")]
        public required string Number { get; init; }

        [JsonPropertyName("appname")]
        public string? AppName { get; set; }
    }

    private sealed class NetGsmResponse
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("jobid")]
        public string? JobId { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }
}
