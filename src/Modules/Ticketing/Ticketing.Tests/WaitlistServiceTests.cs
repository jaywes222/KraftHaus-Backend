using System.Net;
using Common.Application.EventCatalog;
using Common.Application.Notifications;
using Common.Domain.ResultMonad;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Waitlist;
using Ticketing.Domain.Common;
using Ticketing.Domain.Waitlist;
using Xunit;

namespace Ticketing.Tests;

public sealed class WaitlistServiceTests
{
    private readonly FakeCatalog _catalog = new();
    private readonly InMemoryWaitlist _store = new();
    private readonly FakeSender _sender = new();

    [Fact]
    public async Task JoinSavesTheEntryAndSendsOneConfirmation()
    {
        var @event = _catalog.Add(EventLifecycle.Published, DateTimeOffset.UtcNow.AddDays(27));

        var result = await Service().JoinAsync(Request(@event.Slug), CancellationToken.None);

        Assert.False(result.IsFailure);
        Assert.True(result.Value!.SmsSent);

        var entry = Assert.Single(_store.Entries);
        Assert.Equal(result.Value.WaitlistEntryId, entry.Id.Value);
        Assert.Equal("+254712345678", entry.Phone);
        Assert.NotNull(entry.ConfirmationSentAt);
        Assert.Equal(["+254712345678"], _sender.SentTo);
        Assert.Equal(2, _store.Saves);
    }

    [Fact]
    public async Task AFailedSmsStillJoinsAndReportsSmsNotSent()
    {
        var @event = _catalog.Add(EventLifecycle.Published, DateTimeOffset.UtcNow.AddDays(27));
        _sender.Accept = false;

        var result = await Service().JoinAsync(Request(@event.Slug), CancellationToken.None);

        Assert.False(result.Value!.SmsSent);
        Assert.Null(Assert.Single(_store.Entries).ConfirmationSentAt);
    }

    [Fact]
    public async Task TheSamePhoneInAnotherShapeIsAlreadyJoinedAndGetsNoSecondSms()
    {
        var @event = _catalog.Add(EventLifecycle.Published, DateTimeOffset.UtcNow.AddDays(27));
        await Service().JoinAsync(Request(@event.Slug), CancellationToken.None);

        var again = await Service().JoinAsync(Request(@event.Slug) with { Phone = "+254 712 345 678" }, CancellationToken.None);

        Assert.Equal("Ticketing.WaitlistAlreadyJoined", again.Error!.Key);
        Assert.Equal(HttpStatusCode.Conflict, again.Error.StatusCode);
        Assert.Single(_store.Entries);
        Assert.Single(_sender.SentTo);
    }

    [Fact]
    public async Task LosingTheInsertRaceIsAlreadyJoinedAndSendsNothing()
    {
        var @event = _catalog.Add(EventLifecycle.Published, DateTimeOffset.UtcNow.AddDays(27));
        _store.FailNextSaveWith = TicketingErrors.WaitlistAlreadyJoined;

        var result = await Service().JoinAsync(Request(@event.Slug), CancellationToken.None);

        Assert.Equal("Ticketing.WaitlistAlreadyJoined", result.Error!.Key);
        Assert.Empty(_sender.SentTo);
    }

    [Fact]
    public async Task TheSamePhoneMayJoinADifferentEvent()
    {
        var first = _catalog.Add(EventLifecycle.Published, DateTimeOffset.UtcNow.AddDays(27));
        var second = _catalog.Add(EventLifecycle.Published, DateTimeOffset.UtcNow.AddDays(60));

        await Service().JoinAsync(Request(first.Slug), CancellationToken.None);
        var result = await Service().JoinAsync(Request(second.Slug), CancellationToken.None);

        Assert.False(result.IsFailure);
        Assert.Equal(2, _store.Entries.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing-event")]
    public async Task UnknownEventIsNotFound(string? slug)
    {
        var result = await Service().JoinAsync(Request(slug), CancellationToken.None);

        Assert.Equal("Ticketing.EventNotFound", result.Error!.Key);
        Assert.Equal(HttpStatusCode.NotFound, result.Error.StatusCode);
    }

    [Fact]
    public async Task DraftEventIsNotFound()
    {
        var @event = _catalog.Add(EventLifecycle.Draft, DateTimeOffset.UtcNow.AddDays(27));

        var result = await Service().JoinAsync(Request(@event.Slug), CancellationToken.None);

        Assert.Equal("Ticketing.EventNotFound", result.Error!.Key);
    }

    [Theory]
    [InlineData(EventLifecycle.Cancelled, 27)]
    [InlineData(EventLifecycle.Completed, 27)]
    [InlineData(EventLifecycle.Published, -1)]
    public async Task ClosedWaitlistIsAConflictEvenForAnInvalidForm(EventLifecycle status, int daysUntilStart)
    {
        var @event = _catalog.Add(status, DateTimeOffset.UtcNow.AddDays(daysUntilStart));

        var result = await Service().JoinAsync(Request(@event.Slug) with { Phone = "nope" }, CancellationToken.None);

        Assert.Equal("Ticketing.WaitlistClosed", result.Error!.Key);
        Assert.Equal(HttpStatusCode.Conflict, result.Error.StatusCode);
        Assert.Empty(_store.Entries);
    }

    [Fact]
    public async Task InvalidFormIsABadRequestAndNothingIsSaved()
    {
        var @event = _catalog.Add(EventLifecycle.Published, DateTimeOffset.UtcNow.AddDays(27));

        var result = await Service().JoinAsync(
            Request(@event.Slug) with { Phone = "0212345678", Age = 15 },
            CancellationToken.None);

        Assert.Equal("Ticketing.WaitlistInvalid", result.Error!.Key);
        Assert.Equal(HttpStatusCode.BadRequest, result.Error.StatusCode);
        Assert.Contains(result.Error.SubErrors!, x => x.StartsWith("Phone:", StringComparison.Ordinal));
        Assert.Contains(result.Error.SubErrors!, x => x.StartsWith("Age:", StringComparison.Ordinal));
        Assert.Empty(_store.Entries);
        Assert.Empty(_sender.SentTo);
    }

    private WaitlistService Service() =>
        new(_catalog, _store, _store, _sender, Options.Create(new WaitlistOptions()), TimeProvider.System);

    private static JoinWaitlistRequest Request(string? slug) =>
        new(slug, "Wanjiru Kamau", "0712345678", 27, WaitlistGenderInput.Woman, "Kilimani", null, true, "2026-10-01", false);

    private sealed class FakeCatalog : IEventCatalog
    {
        private readonly List<EventSnapshot> _events = [];

        public EventSnapshot Add(EventLifecycle status, DateTimeOffset firstStartsAt)
        {
            var snapshot = new EventSnapshot(
                Guid.CreateVersion7(),
                $"event-{_events.Count + 1}",
                "KraftHaus 001",
                status,
                firstStartsAt,
                firstStartsAt.AddHours(8));
            _events.Add(snapshot);
            return snapshot;
        }

        public Task<EventSnapshot?> FindBySlugAsync(string slug, CancellationToken cancellationToken) =>
            Task.FromResult(_events.Find(x => x.Slug == slug));

        public Task<EventSaleSnapshot?> GetSaleSnapshotAsync(Guid eventId, CancellationToken cancellationToken) =>
            Task.FromResult<EventSaleSnapshot?>(null);
    }

    private sealed class InMemoryWaitlist : IWaitlistRepository, ITicketingUnitOfWork
    {
        private readonly List<WaitlistEntry> _pending = [];

        public List<WaitlistEntry> Entries { get; } = [];

        public int Saves { get; private set; }

        public Error? FailNextSaveWith { get; set; }

        public Task<bool> ExistsAsync(Guid eventId, string phone, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.Exists(x => x.EventId == eventId && x.Phone == phone));

        public Task<WaitlistEntry?> FindAsync(WaitlistEntryId id, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.Find(x => x.Id == id));

        public void Add(WaitlistEntry entry) => _pending.Add(entry);

        public Task<IReadOnlyList<WaitlistEntry>> ListUnconfirmedAsync(
            DateTimeOffset joinedBefore,
            DateTimeOffset joinedAfter,
            int maxAttempts,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WaitlistEntry>>([.. Entries.Where(x => x.ConfirmationSentAt == null && x.ConfirmationAttempts < maxAttempts).Take(limit)]);

        public Task<int> CountAsync(Guid eventId, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.Count(x => x.EventId == eventId));

        public Task<IReadOnlyList<WaitlistEntry>> ListForReadAsync(
            Guid eventId,
            DateTimeOffset? joinedAfter,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WaitlistEntry>>([.. Entries.Where(x => x.EventId == eventId).Take(limit)]);

        public Task<Result<T>> InTransactionAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken) =>
            work(cancellationToken);

        public Task<bool> AlreadyProcessedAsync(Guid messageId, string handler, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public void MarkProcessed(Guid messageId, string handler)
        {
        }

        public Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (FailNextSaveWith is { } error)
            {
                FailNextSaveWith = null;
                _pending.Clear();
                return Task.FromResult<Result>(error);
            }

            Saves++;
            Entries.AddRange(_pending);
            _pending.Clear();
            return Task.FromResult(Result.Success);
        }
    }

    private sealed class FakeSender : IWaitlistConfirmationSender
    {
        public bool Accept { get; set; } = true;

        public List<string> SentTo { get; } = [];

        public Task<bool> SendAsync(WaitlistEntry entry, string eventTitle, CancellationToken cancellationToken)
        {
            SentTo.Add(entry.Phone);
            return Task.FromResult(Accept);
        }
    }
}

public sealed class WaitlistConfirmationSenderTests
{
    [Fact]
    public async Task AGatewayFailureIsReportedAsNotSentInsteadOfThrown()
    {
        var sender = Sender(new ThrowingGateway(), timeoutSeconds: 5);

        Assert.False(await sender.SendAsync(Entry(), "KraftHaus 001", CancellationToken.None));
    }

    [Fact]
    public async Task AGatewayThatHangsTimesOut()
    {
        var sender = Sender(new HangingGateway(), timeoutSeconds: 1);

        Assert.False(await sender.SendAsync(Entry(), "KraftHaus 001", CancellationToken.None));
    }

    [Fact]
    public async Task TheMessageGoesToTheNormalisedPhoneAndNamesTheEvent()
    {
        var gateway = new CapturingGateway();

        Assert.True(await Sender(gateway, timeoutSeconds: 5).SendAsync(Entry(), "KraftHaus 001", CancellationToken.None));

        var message = Assert.Single(gateway.Messages);
        Assert.Equal("+254712345678", message.PhoneNumber);
        Assert.Contains("KraftHaus 001", message.Message, StringComparison.Ordinal);
    }

    private static WaitlistConfirmationSender Sender(ISmsGateway gateway, int timeoutSeconds) =>
        new(
            gateway,
            Options.Create(new WaitlistOptions { ConfirmationSmsTimeoutSeconds = timeoutSeconds }),
            NullLogger<WaitlistConfirmationSender>.Instance);

    private static WaitlistEntry Entry() =>
        WaitlistEntry.Join(
            new WaitlistEntryId(Guid.CreateVersion7()),
            Guid.CreateVersion7(),
            WaitlistEntryTests.Valid(),
            18,
            DateTimeOffset.UtcNow).Value!;

    private sealed class ThrowingGateway : ISmsGateway
    {
        public Task SendAsync(SmsMessage message, CancellationToken cancellationToken) =>
            throw new HttpRequestException("provider down");
    }

    private sealed class HangingGateway : ISmsGateway
    {
        public Task SendAsync(SmsMessage message, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private sealed class CapturingGateway : ISmsGateway
    {
        public List<SmsMessage> Messages { get; } = [];

        public Task SendAsync(SmsMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
