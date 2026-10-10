using System.Net;
using System.Text.Json;
using Common.Application.Notifications;
using Common.Application.Options;
using Common.Infrastructure.Communications.Email;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Common.Tests;

public sealed class BrevoEmailGatewayTests
{
    [Fact]
    public async Task SendsExpectedTransactionalEmailRequest()
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
                    new Uri("https://api.brevo.com/")
            };

        var options =
            Options.Create(
                new EmailOptions
                {
                    Provider = EmailProvider.Brevo,
                    BaseUrl = "https://api.brevo.com/",
                    ApiKey = "test-api-key",
                    FromAddress = "no-reply@example.com",
                    FromName = "KraftHaus",
                    Templates = new EmailTemplatesOptions()
                });

        var gateway =
            new BrevoEmailGateway(
                httpClient,
                options);

        await gateway.SendAsync(
            new EmailMessage(
                "user@example.com",
                "Your verification code",
                "Your code is 123456.",
                "<p>Your code is <strong>123456</strong>.</p>"),
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
            .Be("https://api.brevo.com/v3/smtp/email");

        request.Headers
            .GetValues("api-key")
            .Single()
            .Should()
            .Be("test-api-key");

        var body = capturedBody
            ?? throw new InvalidOperationException(
                "The request body was not captured.");

        using var document = JsonDocument.Parse(body);

        var root = document.RootElement;

        var sender = root.GetProperty("sender");

        sender.GetProperty("email")
            .GetString()
            .Should()
            .Be("no-reply@example.com");

        sender.GetProperty("name")
            .GetString()
            .Should()
            .Be("KraftHaus");

        var recipients = root.GetProperty("to");

        recipients.GetArrayLength()
            .Should()
            .Be(1);

        recipients[0].GetProperty("email")
            .GetString()
            .Should()
            .Be("user@example.com");

        root.GetProperty("subject")
            .GetString()
            .Should()
            .Be("Your verification code");

        root.GetProperty("textContent")
            .GetString()
            .Should()
            .Be("Your code is 123456.");

        root.GetProperty("htmlContent")
            .GetString()
            .Should()
            .Be("<p>Your code is <strong>123456</strong>.</p>");
    }

    [Fact]
    public async Task ThrowsWhenBrevoReturnsFailure()
    {
        using var handler =
            new RecordingHttpMessageHandler(
                _ =>
                    Task.FromResult(
                        new HttpResponseMessage(
                            HttpStatusCode.Unauthorized)
                        {
                            Content =
                                new StringContent(
                                    "invalid api key")
                        }));

        using var httpClient =
            new HttpClient(
                handler,
                disposeHandler: false)
            {
                BaseAddress =
                    new Uri("https://api.brevo.com/")
            };

        var options =
            Options.Create(
                new EmailOptions
                {
                    Provider = EmailProvider.Brevo,
                    BaseUrl = "https://api.brevo.com/",
                    ApiKey = "test-api-key",
                    FromAddress = "no-reply@example.com",
                    FromName = "KraftHaus",
                    Templates = new EmailTemplatesOptions()
                });

        var gateway =
            new BrevoEmailGateway(
                httpClient,
                options);

        var action = () =>
            gateway.SendAsync(
                new EmailMessage(
                    "user@example.com",
                    "Test",
                    "Test body"),
                CancellationToken.None);

        await action.Should()
            .ThrowAsync<HttpRequestException>()
            .WithMessage("*Brevo email request failed*");
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
