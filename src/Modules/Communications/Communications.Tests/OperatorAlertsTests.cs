using Common.Application.Notifications;
using Common.Application.Operations;
using Common.Application.Tenancy;
using Common.Domain.Tenancy;
using Communications.Application.Alerts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Communications.Tests;

public sealed class OperatorAlertsTests
{
    [Fact]
    public async Task NewIssuesAreTextedOnceUntilTheCooldownAndBrokenChecksAreReported()
    {
        var sms = new FakeSms();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var signal = new FakeSignal(new OperationalIssue("payments.stuck", "3 M-Pesa payment(s) pending for over 5 min."));
        var job = Job([signal, new BrokenSignal()], cache, sms, ["+254700000001", "+254700000002"]);

        await job.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, sms.Sent.Count);
        Assert.StartsWith(OperatorAlertsJob.Prefix, sms.Sent[0].Message, StringComparison.Ordinal);
        Assert.Contains("3 M-Pesa payment(s)", sms.Sent[0].Message, StringComparison.Ordinal);
        Assert.Contains("BrokenSignal", sms.Sent[0].Message, StringComparison.Ordinal);

        // Same problems on the next run: nothing new to say.
        await job.ExecuteAsync(CancellationToken.None);
        Assert.Equal(2, sms.Sent.Count);

        // A new problem is sent on its own.
        signal.Issues.Add(new OperationalIssue("payments.review", "1 payment(s) need Finance review."));
        await job.ExecuteAsync(CancellationToken.None);
        Assert.Equal(4, sms.Sent.Count);
        Assert.DoesNotContain("pending", sms.Sent[^1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenNobodyCouldBeReachedTheNextRunTriesAgain()
    {
        var sms = new FakeSms { Fail = true };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var job = Job([new FakeSignal(new OperationalIssue("messages.failing", "SMS failing."))], cache, sms, ["+254700000001"]);

        await job.ExecuteAsync(CancellationToken.None);
        sms.Fail = false;
        await job.ExecuteAsync(CancellationToken.None);

        Assert.Single(sms.Sent);
    }

    [Fact]
    public void OptionsRefuseNumbersThatAreNotE164()
    {
        var validator = new OperationalAlertOptionsValidator();

        Assert.True(validator.Validate(null, new OperationalAlertOptions { Phones = ["0712345678"] }).Failed);
        Assert.True(validator.Validate(null, new OperationalAlertOptions { Phones = ["+254712345678"], Emails = ["ops@krafthaus.co.ke"] }).Succeeded);
    }

    private static OperatorAlertsJob Job(IEnumerable<IOperationalSignal> signals, IMemoryCache cache, FakeSms sms, IReadOnlyList<string> phones) =>
        new(signals, new FakeTenant(), cache, sms, new FakeEmail(), Options.Create(new OperationalAlertOptions { Phones = phones }), NullLogger<OperatorAlertsJob>.Instance);

    private sealed class FakeSignal(params OperationalIssue[] issues) : IOperationalSignal
    {
        public List<OperationalIssue> Issues { get; } = [.. issues];

        public Task<IReadOnlyList<OperationalIssue>> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OperationalIssue>>([.. Issues]);
    }

    private sealed class BrokenSignal : IOperationalSignal
    {
        public Task<IReadOnlyList<OperationalIssue>> CheckAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("database down");
    }

    private sealed class FakeSms : ISmsGateway
    {
        public List<SmsMessage> Sent { get; } = [];

        public bool Fail { get; set; }

        public Task SendAsync(SmsMessage message, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new HttpRequestException("down");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmail : IEmailGateway
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeTenant : ICurrentTenant
    {
        public bool IsResolved => true;

        public TenantId TenantId { get; } = new(Guid.CreateVersion7());

        public IdentityScope IdentityScope => IdentityScope.Platform;
    }
}
