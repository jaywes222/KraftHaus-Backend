using System.Net;
using System.Net.Http;
using System.Text.Json;
using Common.Application.Notifications;
using Common.Application.Options;
using Common.Infrastructure.Notifications.Sms;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Common.Tests;

public sealed class MobileSasaSmsGatewayTests
{
    [Fact]
    public async Task SendsExpectedRequest()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        using var handler =
            new RecordingHttpMessageHandler(
                async request =>
                {
                    capturedRequest = request;

                    var content = request.Content
                        ?? throw new InvalidOperationException(
                            "The request content was not set.");

                    capturedBody =
                        await content.ReadAsStringAsync();

                    return new HttpResponseMessage(
                        HttpStatusCode.OK);
                });

        using var httpClient =
            new HttpClient(
                handler,
                disposeHandler: false)
            {
                BaseAddress =
                    new Uri(
                        "https://api.mobilesasa.com/")
            };

        var options =
            Options.Create(
                new SmsOptions
                {
                    Provider = SmsProvider.MobileSasa,
                    BaseUrl = "https://api.mobilesasa.com/",
                    ApiKey = "test-api-key",
                    SenderId = "KRAFT",
                    ThrottleCounterTtlHours = 48,
                    Templates = new SmsTemplatesOptions()
                });

        var gateway =
            new MobileSasaSmsGateway(
                httpClient,
                options);

        await gateway.SendAsync(
            new SmsMessage(
                "+254700000000",
                "Your code is 123456."),
            CancellationToken.None);

        var request = capturedRequest
            ?? throw new InvalidOperationException(
                "The HTTP request was not captured.");

        request.Method
            .Should()
            .Be(HttpMethod.Post);

        var requestUri = request.RequestUri
            ?? throw new InvalidOperationException(
                "The request URI was not set.");

        requestUri.ToString()
            .Should()
            .Be(
                "https://api.mobilesasa.com/v1/send/message");

        var authorization =
            request.Headers.Authorization
            ?? throw new InvalidOperationException(
                "The Authorization header was not set.");

        authorization.Scheme
            .Should()
            .Be("Bearer");

        authorization.Parameter
            .Should()
            .Be("test-api-key");

        var body = capturedBody
            ?? throw new InvalidOperationException(
                "The request body was not captured.");

        using var document = JsonDocument.Parse(body);

        var root = document.RootElement;

        root.GetProperty("senderID")
            .GetString()
            .Should()
            .Be("KRAFT");

        root.GetProperty("phone")
            .GetString()
            .Should()
            .Be("+254700000000");

        root.GetProperty("message")
            .GetString()
            .Should()
            .Be("Your code is 123456.");
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return handler(request);
        }
    }
}
