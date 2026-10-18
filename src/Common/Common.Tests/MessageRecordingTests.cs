using Common.Application.Notifications;
using Common.Infrastructure.Notifications;
using Common.Infrastructure.Notifications.Sms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Common.Tests;

public sealed class MessageRecordingTests
{
    [Fact]
    public async Task SendsAreRecordedAndFailuresStillReachTheCaller()
    {
        var recorder = new FakeRecorder();
        var inner = new FakeSms();
        using var services = new ServiceCollection().AddSingleton<IMessageRecorder>(recorder).BuildServiceProvider();
        var gateway = new RecordingSmsGateway(inner, services, NullLogger<RecordingSmsGateway>.Instance);

        await gateway.SendAsync(new SmsMessage("+254712345678", "Hello"), CancellationToken.None);
        inner.Fail = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => gateway.SendAsync(new SmsMessage("+254712345678", "Again"), CancellationToken.None));

        Assert.Equal([("Hello", "id-1", null), ("Again", null, "HttpRequestException")], recorder.Sms);
    }

    [Fact]
    public async Task ABroadcastSenderCanTurnRecordingOffAndARecorderFailureIsSwallowed()
    {
        var recorder = new FakeRecorder();
        using var services = new ServiceCollection().AddSingleton<IMessageRecorder>(recorder).BuildServiceProvider();
        var gateway = new RecordingSmsGateway(new FakeSms(), services, NullLogger<RecordingSmsGateway>.Instance);

        using (MessageRecording.Suppress())
        {
            await gateway.SendAsync(new SmsMessage("+254712345678", "Broadcast"), CancellationToken.None);
        }

        Assert.Empty(recorder.Sms);
        Assert.False(MessageRecording.IsSuppressed);

        recorder.Throw = true;
        await gateway.SendAsync(new SmsMessage("+254712345678", "Still sent"), CancellationToken.None);
    }

    [Fact]
    public async Task WithoutCommunicationsNothingIsRecorded()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var gateway = new RecordingSmsGateway(new FakeSms(), services, NullLogger<RecordingSmsGateway>.Instance);

        var receipt = await gateway.SendWithReceiptAsync(new SmsMessage("+254712345678", "Hi"), CancellationToken.None);

        Assert.Equal("id-1", receipt.ProviderMessageId);
    }

    [Fact]
    public void TheDecoratorWrapsWhateverGatewayWasRegistered()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddScoped<ISmsGateway, FakeSms>()
            .AddMessageRecording()
            .BuildServiceProvider();

        using var scope = services.CreateScope();
        Assert.IsType<RecordingSmsGateway>(scope.ServiceProvider.GetRequiredService<ISmsGateway>());
    }

    [Theory]
    [InlineData("""{"status":true,"responseCode":"0200","messageId":"abc-123"}""", "abc-123")]
    [InlineData("""{"status":true,"data":{"message_id":"9f1"}}""", "9f1")]
    [InlineData("""{"responses":[{"messageId":42}]}""", "42")]
    [InlineData("""{"status":true}""", null)]
    [InlineData("OK", null)]
    [InlineData("", null)]
    public void TheProviderMessageIdIsFoundWhereverMobileSasaPutsIt(string body, string? expected) =>
        Assert.Equal(expected, MobileSasaSmsGateway.FindMessageId(body));

    private sealed class FakeSms : ISmsGateway
    {
        private int _count;

        public bool Fail { get; set; }

        public Task SendAsync(SmsMessage message, CancellationToken cancellationToken) =>
            Fail ? throw new HttpRequestException("down") : Task.CompletedTask;

        public async Task<SmsReceipt> SendWithReceiptAsync(SmsMessage message, CancellationToken cancellationToken)
        {
            await SendAsync(message, cancellationToken);
            return new SmsReceipt($"id-{++_count}");
        }
    }

    private sealed class FakeRecorder : IMessageRecorder
    {
        public List<(string Body, string? ProviderId, string? Error)> Sms { get; } = [];

        public bool Throw { get; set; }

        public Task RecordSmsAsync(SmsMessage message, SmsReceipt? receipt, string? error, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new InvalidOperationException("database down");
            }

            Sms.Add((message.Message, receipt?.ProviderMessageId, error));
            return Task.CompletedTask;
        }

        public Task RecordEmailAsync(EmailMessage message, string? error, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
