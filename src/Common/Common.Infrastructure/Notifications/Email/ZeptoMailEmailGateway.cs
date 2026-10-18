using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Common.Application.Notifications;
using Common.Application.Options;
using Microsoft.Extensions.Options;

namespace Common.Infrastructure.Communications.Email;

/// <summary>
///     Zoho ZeptoMail transactional e-mail (<c>POST v1.1/email</c>). <see cref="EmailOptions.ApiKey" /> is the Mail
///     Agent's "Send Mail token", with or without its <c>Zoho-enczapikey</c> prefix. <see cref="EmailOptions.BaseUrl" />
///     is the API host of the account's data centre (<c>https://api.zeptomail.com/</c>, <c>.eu</c>, <c>.in</c>, ...).
/// </summary>
public sealed class ZeptoMailEmailGateway(
    HttpClient httpClient,
    IOptions<EmailOptions> options)
    : IEmailGateway
{
    private const string AuthorizationScheme = "Zoho-enczapikey";

    private readonly EmailOptions _options =
        options.Value;

    public async Task SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "v1.1/email");

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                AuthorizationScheme,
                Token(_options.ApiKey));

        request.Content =
            JsonContent.Create(
                new ZeptoMailRequest(
                    new ZeptoMailAddress(_options.FromAddress, _options.FromName),
                    [new ZeptoMailRecipient(new ZeptoMailAddress(message.To, null))],
                    message.Subject,
                    message.TextBody,
                    message.HtmlBody));

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
                $"ZeptoMail email request failed with " +
                $"status {(int)response.StatusCode}: {body}");
        }
    }

    /// <summary>The console shows the token as "Zoho-enczapikey &lt;token&gt;"; accept it pasted either way.</summary>
    private static string Token(string apiKey)
    {
        var key = apiKey.Trim();

        return key.StartsWith(AuthorizationScheme + " ", StringComparison.OrdinalIgnoreCase)
            ? key[(AuthorizationScheme.Length + 1)..].Trim()
            : key;
    }

    private sealed record ZeptoMailRequest(
        [property: JsonPropertyName("from")] ZeptoMailAddress From,
        [property: JsonPropertyName("to")] IReadOnlyList<ZeptoMailRecipient> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("textbody")] string TextBody,
        [property: JsonPropertyName("htmlbody")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? HtmlBody);

    private sealed record ZeptoMailRecipient(
        [property: JsonPropertyName("email_address")] ZeptoMailAddress EmailAddress);

    private sealed record ZeptoMailAddress(
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("name")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Name);
}
