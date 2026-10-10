using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Api.IntegrationTests.Identity.Auditing;

/// <summary>
///     The Identity HTTP surface as the integration tests drive it. Every call carries a W3C <c>traceparent</c> so
///     the audit rows it produces can be selected by trace id.
/// </summary>
internal static class IdentityApi
{
    public const string DisplayName = "Wanjiku Kamau";

    public const string DeviceId = "it-device-1";

    public static async Task<string> RequestOtpAsync(
        HttpClient client,
        string phone,
        ActivityTraceId trace)
    {
        using var response = await SendAsync(
            client,
            "/api/identity/otp/request",
            new { channel = "Phone", destination = phone, purpose = "SignIn" },
            trace);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("challengeId").GetString()!;
    }

    public static Task<HttpResponseMessage> VerifyAsync(
        HttpClient client,
        string challengeId,
        string code,
        ActivityTraceId trace) =>
        SendAsync(
            client,
            "/api/identity/otp/verify",
            new
            {
                challengeId,
                code,
                deviceId = DeviceId,
                displayName = DisplayName,
                acceptTerms = true,
                acceptPrivacy = true
            },
            trace);

    public static Task<HttpResponseMessage> RefreshAsync(
        HttpClient client,
        string sessionId,
        string refreshToken,
        ActivityTraceId trace) =>
        SendAsync(
            client,
            "/api/identity/session/refresh",
            new { sessionId, refreshToken },
            trace);

    public static Task<HttpResponseMessage> RevokeAsync(
        HttpClient client,
        string? accessToken,
        ActivityTraceId trace) =>
        SendAsync(
            client,
            "/api/identity/session/revoke",
            body: null,
            trace,
            accessToken);

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string path,
        object? body,
        ActivityTraceId trace,
        string? accessToken = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        request.Headers.Add(
            "traceparent",
            $"00-{trace.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");

        return await client.SendAsync(request);
    }

    /// <summary>Any method, optional bearer token, optional Host header (to call as another tenant's site).</summary>
    public static async Task<HttpResponseMessage> CallAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body = null,
        string? accessToken = null,
        string? host = null)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (host is not null)
        {
            request.Headers.Host = host;
        }

        return await client.SendAsync(request);
    }

    /// <summary>Signs a new phone user in on the default (localhost) tenant host.</summary>
    public static async Task<IdentityLogin> SignInAsync(
        HttpClient client,
        CapturingOtpSender otpSender,
        string? phone = null)
    {
        phone ??= NewKenyanPhone();
        var trace = ActivityTraceId.CreateRandom();
        var challengeId = await RequestOtpAsync(client, phone, trace);

        using var response = await VerifyAsync(client, challengeId, otpSender.CodeFor(phone), trace);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return new IdentityLogin(
            body.GetProperty("userId").GetGuid(),
            phone,
            body.GetProperty("sessionId").GetString()!,
            body.GetProperty("accessToken").GetString()!,
            body.GetProperty("refreshToken").GetString()!);
    }

    public static string NewKenyanPhone() =>
        "+2547" + RandomNumberGenerator.GetInt32(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);
}

internal sealed record IdentityLogin(
    Guid UserId,
    string Phone,
    string SessionId,
    string AccessToken,
    string RefreshToken);

/// <summary>
///     One host + database for every Identity HTTP test class: they run sequentially and never boot two
///     <c>WebApplicationFactory</c> instances in parallel (shared static logger / ActivitySource state).
/// </summary>
[CollectionDefinition(Name)]
public sealed class IdentityApiCollection : ICollectionFixture<IdentityAuditApiFixture>
{
    public const string Name = "IdentityApi";
}
