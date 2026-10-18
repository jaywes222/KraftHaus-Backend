using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Payments.Application.Provider;
using Payments.Domain.Payments;
using Payments.Infrastructure.PalPluss;
using Xunit;

namespace Payments.Tests;

public sealed class PalPlussClientTests
{
    [Fact]
    public async Task StkPushSendsBasicAuthAndTheDocumentedBody()
    {
        using var handler = new StubHandler(HttpStatusCode.OK,
            """{"success":true,"data":{"transactionId":"fa98a577-95ea-4a8f-8467-1fbe74f5d6f4","status":"PENDING","providerCheckoutId":"ws_CO_1"},"requestId":"r1"}""");

        var result = await Client(handler).StartStkAsync(Request(), CancellationToken.None);

        var accepted = Assert.IsType<ProviderStartResult.Accepted>(result);
        Assert.Equal("fa98a577-95ea-4a8f-8467-1fbe74f5d6f4", accepted.TransactionId);
        Assert.Equal("ws_CO_1", accepted.CheckoutId);

        Assert.Equal(HttpMethod.Post, handler.Sent!.Method);
        Assert.Equal("https://sandbox.palpluss.com/v1/payments/stk", handler.Sent.RequestUri!.ToString());
        Assert.Equal("Basic", handler.Sent.Headers.Authorization!.Scheme);
        Assert.Equal("pk_test_key:", Encoding.UTF8.GetString(Convert.FromBase64String(handler.Sent.Headers.Authorization.Parameter!)));

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(1500m, body.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("254712345678", body.RootElement.GetProperty("phone").GetString());
        Assert.Equal("7K3M9Q2B", body.RootElement.GetProperty("accountReference").GetString());
        Assert.Equal("Order 7K3M9Q", body.RootElement.GetProperty("transactionDesc").GetString());
        Assert.Equal("https://events.example/webhooks/palpluss/abc", body.RootElement.GetProperty("callbackUrl").GetString());
    }

    [Fact]
    public async Task RefusalsCarryTheCodeAndRetryAfter()
    {
        using var handler = new StubHandler((HttpStatusCode)429,
            """{"success":false,"error":{"code":"STK_TEMP_BANNED","message":"Paused","details":{"retryAfterSeconds":120}},"requestId":"r2"}""");

        var refused = Assert.IsType<ProviderStartResult.Refused>(await Client(handler).StartStkAsync(Request(), CancellationToken.None));

        Assert.Equal("STK_TEMP_BANNED", refused.ErrorCode);
        Assert.Equal(120, refused.RetryAfterSeconds);
    }

    [Fact]
    public async Task ServerErrorsAndTimeoutsAreUnknownNotRefused()
    {
        using var handler = new StubHandler(HttpStatusCode.BadGateway, "<html>");
        Assert.IsType<ProviderStartResult.Unknown>(await Client(handler).StartStkAsync(Request(), CancellationToken.None));

        using var slow = new StubHandler(HttpStatusCode.OK, "{}", delay: TimeSpan.FromSeconds(10));
        Assert.IsType<ProviderStartResult.Unknown>(await Client(slow, timeout: TimeSpan.FromMilliseconds(200)).StartStkAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task TransactionLookupParsesCamelCaseRecords()
    {
        using var handler = new StubHandler(HttpStatusCode.OK,
            """{"success":true,"data":{"transactionId":"tx-1","status":"SUCCESS","amount":1500,"accountReference":"7K3M9Q2B","resultCode":"0","resultDesc":"ok","mpesaReceipt":"QK12ABC"}}""");

        var found = Assert.IsType<ProviderLookup.Found>(await Client(handler).GetTransactionAsync("tx-1", CancellationToken.None));

        Assert.Equal(ProviderTransactionStatus.Success, found.Record.Status);
        Assert.Equal(1500m, found.Record.Amount);
        Assert.Equal("QK12ABC", found.Record.MpesaReceipt);
        Assert.Equal("https://sandbox.palpluss.com/v1/transactions/tx-1", handler.Sent!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("""{"transactionId":"other","status":"SUCCESS","amount":1}""")]
    [InlineData("""{"transactionId":"tx-1","status":"MAYBE","amount":1}""")]
    [InlineData("""{"transactionId":"tx-1","status":"SUCCESS"}""")]
    public void RecordsThatDoNotMatchAreRejected(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Null(PalPlussClient.ParseRecord(document.RootElement, "tx-1"));
    }

    [Fact]
    public async Task NotFoundAndOutagesAreDistinguished()
    {
        using var missing = new StubHandler(HttpStatusCode.NotFound, """{"success":false,"error":{"code":"TRANSACTION_NOT_FOUND"}}""");
        Assert.IsType<ProviderLookup.NotFound>(await Client(missing).GetTransactionAsync("tx-1", CancellationToken.None));

        using var down = new StubHandler(HttpStatusCode.ServiceUnavailable, "");
        Assert.IsType<ProviderLookup.Unavailable>(await Client(down).GetTransactionAsync("tx-1", CancellationToken.None));
    }

    private static ProviderStkRequest Request() =>
        new(1500m, "+254712345678", "7K3M9Q2B", "Order 7K3M9Q", new Uri("https://events.example/webhooks/palpluss/abc"));

    private static PalPlussClient Client(StubHandler handler, TimeSpan? timeout = null)
    {
        var options = new PalPlussOptions { Mode = PalPlussMode.Sandbox, ApiKey = "pk_test_key" };
#pragma warning disable CA2000 // The HttpClient does not own the handler; the test disposes it.
        var http = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = options.ResolveBaseUrl(),
            Timeout = timeout ?? TimeSpan.FromSeconds(5)
        };
#pragma warning restore CA2000
        return new PalPlussClient(http, Options.Create(options), NullLogger<PalPlussClient>.Instance);
    }

    private sealed class StubHandler(HttpStatusCode status, string body, TimeSpan? delay = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Sent { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (delay is { } wait)
            {
                await Task.Delay(wait, cancellationToken);
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
