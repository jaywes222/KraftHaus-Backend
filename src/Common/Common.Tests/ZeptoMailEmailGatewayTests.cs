using System.Net;
using System.Text.Json;
using Common.Application.Notifications;
using Common.Application.Options;
using Common.Infrastructure.Communications.Email;
using Microsoft.Extensions.Options;
using Xunit;

namespace Common.Tests;

public sealed class ZeptoMailEmailGatewayTests
{
    [Theory]
    [InlineData("send-mail-token")]
    [InlineData("Zoho-enczapikey send-mail-token")]
    public async Task SendsTheZeptoMailRequest(string apiKey)
    {
        HttpRequestMessage? captured = null;
        string? body = null;

        using var handler = new RecordingHandler(async request =>
        {
            captured = request;
            body = await (request.Content ?? throw new InvalidOperationException("No body.")).ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        await Gateway(handler, apiKey).SendAsync(
            new EmailMessage(
                "user@example.com",
                "Your KRAFTHAUS code",
                "Your code is 123456.",
                "<p>Your code is <strong>123456</strong>.</p>"),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("https://api.zeptomail.com/v1.1/email", captured.RequestUri!.ToString());
        Assert.Equal("Zoho-enczapikey", captured.Headers.Authorization!.Scheme);
        Assert.Equal("send-mail-token", captured.Headers.Authorization.Parameter);

        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;

        Assert.Equal("no-reply@krafthaus.co.ke", root.GetProperty("from").GetProperty("address").GetString());
        Assert.Equal("KRAFTHAUS", root.GetProperty("from").GetProperty("name").GetString());

        var to = Assert.Single(root.GetProperty("to").EnumerateArray());
        var address = to.GetProperty("email_address");
        Assert.Equal("user@example.com", address.GetProperty("address").GetString());
        Assert.False(address.TryGetProperty("name", out _));

        Assert.Equal("Your KRAFTHAUS code", root.GetProperty("subject").GetString());
        Assert.Equal("Your code is 123456.", root.GetProperty("textbody").GetString());
        Assert.Equal("<p>Your code is <strong>123456</strong>.</p>", root.GetProperty("htmlbody").GetString());
    }

    [Fact]
    public async Task LeavesOutTheHtmlBodyWhenThereIsNone()
    {
        string? body = null;

        using var handler = new RecordingHandler(async request =>
        {
            body = await (request.Content ?? throw new InvalidOperationException("No body.")).ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Gateway(handler, "send-mail-token").SendAsync(
            new EmailMessage("user@example.com", "Subject", "Text only"),
            CancellationToken.None);

        using var document = JsonDocument.Parse(body!);
        Assert.False(document.RootElement.TryGetProperty("htmlbody", out _));
    }

    [Fact]
    public async Task ThrowsWhenZeptoMailRefuses()
    {
        using var handler = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":{"code":"TM_3201","message":"Invalid API Token found"}}""")
            }));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Gateway(handler, "wrong").SendAsync(
                new EmailMessage("user@example.com", "Subject", "Body"),
                CancellationToken.None));

        Assert.Contains("ZeptoMail email request failed with status 401", error.Message, StringComparison.Ordinal);
    }

    private static ZeptoMailEmailGateway Gateway(HttpMessageHandler handler, string apiKey)
    {
#pragma warning disable CA2000 // The HttpClient lives as long as the gateway under test; the handler is disposed by the test.
        var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.zeptomail.com/") };
#pragma warning restore CA2000

        return new ZeptoMailEmailGateway(
            client,
            Options.Create(new EmailOptions
            {
                Provider = EmailProvider.ZeptoMail,
                BaseUrl = "https://api.zeptomail.com/",
                ApiKey = apiKey,
                FromAddress = "no-reply@krafthaus.co.ke",
                FromName = "KRAFTHAUS",
                Templates = new EmailTemplatesOptions()
            }));
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }
}
