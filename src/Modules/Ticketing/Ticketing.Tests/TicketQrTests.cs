using Microsoft.Extensions.Options;
using Ticketing.Application.Tickets;
using Ticketing.Domain.Common;
using Ticketing.Domain.Tickets;
using Xunit;

namespace Ticketing.Tests;

public sealed class TicketQrTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 18, 0, 0, TimeSpan.Zero);

    private static TicketQrCodec Codec(string active = "k1") =>
        new(Options.Create(new TicketQrOptions
        {
            ActiveKeyId = active,
            Keys = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["k1"] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()),
                ["k2"] = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray())
            }
        }));

    private static Ticket NewTicket() =>
        Ticket.Issue(new TicketId(Guid.CreateVersion7()), new TicketIssue(
            new OrderId(Guid.NewGuid()), new OrderLineId(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            "+254712345678", "Wanjiru", "KraftHaus 001", "Regular", null, Now, null, null), TicketStatus.Valid, Now);

    [Fact]
    public void IssuedCodesReadBack()
    {
        var ticket = NewTicket();
        var payload = Codec().Issue(ticket);

        var content = Codec().Read(payload, Now)!;

        Assert.StartsWith("KH1.k1.", payload, StringComparison.Ordinal);
        Assert.True(payload.Length < 120);
        Assert.Equal(ticket.Id.Value, content.TicketId);
        Assert.Equal(ticket.EventDateId, content.EventDateId);
        Assert.Equal(ticket.Code, content.Code);
    }

    [Fact]
    public void TamperedForgedOrExpiredCodesAreRejected()
    {
        var payload = Codec().Issue(NewTicket());
        var parts = payload.Split('.');

        var flipped = parts[2][..^1] + (parts[2][^1] == 'A' ? 'B' : 'A');
        Assert.Null(Codec().Read($"{parts[0]}.{parts[1]}.{flipped}.{parts[3]}", Now));
        Assert.Null(Codec().Read($"{parts[0]}.k2.{parts[2]}.{parts[3]}", Now));
        Assert.Null(Codec().Read($"{parts[0]}.k9.{parts[2]}.{parts[3]}", Now));
        Assert.Null(Codec().Read("not a code", Now));
        Assert.Null(Codec().Read(payload, Now.AddDays(3)));
    }

    [Fact]
    public void RotatedKeysStillReadOldCodes()
    {
        var old = Codec("k1").Issue(NewTicket());

        Assert.NotNull(Codec("k2").Read(old, Now));
    }

    [Fact]
    public void TransferChangesTheCodeInsideTheQr()
    {
        var ticket = NewTicket();
        var before = Codec().Read(Codec().Issue(ticket), Now)!;

        ticket.Transfer("+254722000111", "Achieng", 1);

        Assert.NotEqual(before.Code, ticket.Code);
    }

    [Fact]
    public void QrAndCodeShowFromMidnightNairobiOnTheEventDay()
    {
        // The date starts 7 Nov 18:00 UTC (21:00 in Nairobi); the Nairobi day starts 6 Nov 21:00 UTC.
        var ticket = NewTicket();
        var midnight = new DateTimeOffset(2026, 11, 6, 21, 0, 0, TimeSpan.Zero);

        var before = MyTicketView.For(ticket, Codec(), midnight.AddMinutes(-1));
        var after = MyTicketView.For(ticket, Codec(), midnight);

        Assert.Equal(midnight, ticket.GoesLiveAt);
        Assert.Null(before.QrPayload);
        Assert.Null(before.Code);
        Assert.True(before.CanTransfer);
        Assert.Equal(midnight, before.LiveAt);

        Assert.StartsWith("KH1.", after.QrPayload, StringComparison.Ordinal);
        Assert.Equal(ticket.Code, after.Code);
        Assert.False(after.CanTransfer);
    }

    [Fact]
    public void WithoutTheEventDayRuleTheQrShowsAtOnceAndTransfersRunToTheStart()
    {
        var ticket = NewTicket();
        var codec = new TicketQrCodec(Options.Create(new TicketQrOptions
        {
            ActiveKeyId = "k1",
            LiveOnEventDay = false,
            Keys = new Dictionary<string, string>(StringComparer.Ordinal) { ["k1"] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()) }
        }));

        // Issued at the date's start in this fixture: live at issue, transfers closed at the start.
        var view = MyTicketView.For(ticket, codec, Now);

        Assert.NotNull(view.QrPayload);
        Assert.Equal(ticket.IssuedAt, view.LiveAt);
        Assert.Equal(ticket.DateStartsAt, codec.TransfersCloseAt(ticket));
    }
}
