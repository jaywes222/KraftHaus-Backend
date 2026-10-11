using Common.Domain.Currency;
using Common.Domain.ResultMonad;
using Events.Domain.Common;
using Events.Domain.DomainEvents;
using Events.Domain.Model;
using Events.Domain.Venues;
using Xunit;

namespace Events.Tests;

/// <summary>The event aggregate's rules (UF-016), without a database.</summary>
public sealed class EventRulesTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _night = _now.AddDays(28).AddHours(9); // 18:00 UTC on 7 Nov

    [Theory]
    [InlineData("krafthaus-001")]
    [InlineData("abc")]
    [InlineData("event2026")]
    public void SlugValid(string slug) => Assert.False(Event.ValidateSlug(slug).IsFailure);

    [Theory]
    [InlineData("ab")]
    [InlineData("KraftHaus")]
    [InlineData("kraft--haus")]
    [InlineData("-kraft")]
    [InlineData("kraft haus")]
    public void SlugInvalid(string slug) => Assert.Equal("Events.SlugInvalid", Event.ValidateSlug(slug).Error!.Key);

    [Fact]
    public void CreateStartsAsDraftWithDefaultPolicies()
    {
        var @event = NewEvent();

        Assert.Equal(EventStatus.Draft, @event.Status);
        Assert.False(@event.Policies.RefundsEnabled);
        Assert.Equal(EventPolicies.DefaultHoldMinutes, @event.Policies.HoldMinutes);
        Assert.IsType<EventCreated>(Assert.Single(@event.Events));
    }

    [Fact]
    public void DatesMustBeFutureAndMustNotOverlap()
    {
        var @event = NewEvent();

        Assert.Equal("Events.DateStartInvalid", AddDate(@event, _now.AddHours(-1)).Error!.Key);
        Assert.False(AddDate(@event, _night).IsFailure);
        Assert.Equal("Events.DateOverlaps", AddDate(@event, _night.AddHours(2)).Error!.Key);
        Assert.False(AddDate(@event, _night.AddDays(1)).IsFailure);
    }

    [Fact]
    public void EachDateHasItsOwnTiers()
    {
        var @event = NewEvent();
        var day1 = AddDate(@event, _night).Value!;
        var day2 = AddDate(@event, _night.AddDays(1)).Value!;

        Assert.False(AddTier(@event, day1, "Regular", 500).IsFailure);
        Assert.False(AddTier(@event, day2, "Regular", 300).IsFailure);
        Assert.Equal("Events.TierNameTaken", AddTier(@event, day1, "regular", 10).Error!.Key);

        Assert.Equal(500, Assert.Single(day1.Tiers).Capacity);
        Assert.Equal(300, Assert.Single(day2.Tiers).Capacity);
    }

    [Fact]
    public void SalePhasesOfATierCannotOverlapAndMustEndByTheDateEnd()
    {
        var @event = NewEvent();
        var date = AddDate(@event, _night).Value!;
        var tier = AddTier(@event, date, "Regular", 500).Value!;

        Assert.False(AddPhase(@event, date, tier, "Early Bird", 1500, _now, _now.AddDays(7)).IsFailure);
        Assert.Equal(
            "Events.PhaseOverlaps",
            AddPhase(@event, date, tier, "Advance", 2000, _now.AddDays(6), _now.AddDays(10)).Error!.Key);
        Assert.False(AddPhase(@event, date, tier, "Advance", 2000, _now.AddDays(7), _night).IsFailure);
        Assert.Equal(
            "Events.SalesWindowInvalid",
            AddPhase(@event, date, tier, "Gate", 2500, _night, _night.AddDays(2)).Error!.Key);

        Assert.Equal("Early Bird", tier.CurrentPhase(_now.AddDays(1))!.Name);
        Assert.Equal("Advance", tier.CurrentPhase(_now.AddDays(8))!.Name);
        Assert.Equal("Advance", tier.NextPhase(_now.AddDays(1))!.Name);
    }

    [Theory]
    [InlineData(1500.50)]
    [InlineData(0)]
    [InlineData(-100)]
    public void PricesArePositiveWholeShillings(decimal amount)
    {
        var @event = NewEvent();
        var date = AddDate(@event, _night).Value!;
        var tier = AddTier(@event, date, "Regular", 500).Value!;

        var result = @event.AddPhase(
            date.Id,
            tier.Id,
            new SalePhaseId(Guid.CreateVersion7()),
            new PhaseSpec("Early Bird", Money.Kes(amount).Value, _now, _now.AddDays(1), null),
            _now);

        Assert.Equal("Events.PriceInvalid", result.Error!.Key);
    }

    [Fact]
    public void CoupleOfferAdmitsExactlyTwoGangOfferAdmitsThreeOrMore()
    {
        var @event = NewEvent();
        var date = AddDate(@event, _night).Value!;
        var tier = AddTier(@event, date, "Regular", 500).Value!;

        Assert.Equal("Events.AdmitsInvalid", AddOffer(@event, date, tier, GroupOfferKind.Couple, "Couple", 3).Error!.Key);
        Assert.Equal("Events.AdmitsInvalid", AddOffer(@event, date, tier, GroupOfferKind.Gang, "Gang", 2).Error!.Key);
        Assert.False(AddOffer(@event, date, tier, GroupOfferKind.Couple, "Couple", 2).IsFailure);
        Assert.False(AddOffer(@event, date, tier, GroupOfferKind.Gang, "Gang Gang", 6).IsFailure);
        Assert.Equal(2, tier.Offers.Count);
    }

    [Fact]
    public void OfferAllocationMustFitTheTier()
    {
        var @event = NewEvent();
        var date = AddDate(@event, _night).Value!;
        var tier = AddTier(@event, date, "Regular", 10).Value!;

        var tooMany = @event.AddGroupOffer(
            date.Id,
            tier.Id,
            new GroupOfferId(Guid.CreateVersion7()),
            new OfferSpec(GroupOfferKind.Gang, "Gang", 5, Money.KesWhole(6000), _now, _night, Allocation: 3),
            _now);

        Assert.Equal("Events.AllocationInvalid", tooMany.Error!.Key);
    }

    [Fact]
    public void PublishListsEveryProblemThenSucceedsWhenReady()
    {
        var @event = NewEvent();
        var date = AddDate(@event, _night).Value!;
        var tier = AddTier(@event, date, "Regular", 500).Value!;

        var problems = @event.Publish(_now).Error!;
        Assert.Equal("Events.NotReadyToPublish", problems.Key);
        Assert.Contains("Set a venue.", problems.SubErrors!);
        Assert.Contains(problems.SubErrors!, x => x.Contains("no sale phase", StringComparison.Ordinal));

        Assert.False(@event.UseInlineVenue(Details("KICC")).IsFailure);
        Assert.False(AddPhase(@event, date, tier, "Early Bird", 1500, _now, _now.AddDays(7)).IsFailure);

        Assert.False(@event.Publish(_now).IsFailure);
        Assert.Equal(EventStatus.Published, @event.Status);
        Assert.Equal(_now, @event.PublishedAt);
    }

    [Fact]
    public void TierWithOnlyAGroupOfferCanBePublished()
    {
        var @event = ReadyEvent(out var date, out _);
        var couples = AddTier(@event, date, "Couples only", 100).Value!;

        Assert.False(AddOffer(@event, date, couples, GroupOfferKind.Couple, "Couple", 2).IsFailure);
        Assert.False(@event.Publish(_now).IsFailure);
    }

    [Fact]
    public void AfterPublishingSoldStructureIsProtected()
    {
        var @event = ReadyEvent(out var date, out var tier);
        var phase = tier.Phases[0];
        Assert.False(@event.Publish(_now).IsFailure);

        var later = _now.AddDays(1);

        Assert.Equal("Events.TierFrozen", @event.RemoveTier(date.Id, tier.Id).Error!.Key);
        Assert.Equal("Events.DateFrozen", @event.RemoveDate(date.Id).Error!.Key);
        Assert.Equal("Events.VenueFrozen", @event.UseInlineVenue(Details("Elsewhere")).Error!.Key);
        Assert.Equal(
            "Events.CapacityReduced",
            @event.UpdateTier(date.Id, tier.Id, new TierSpec("Regular", null, 100)).Error!.Key);
        Assert.False(@event.UpdateTier(date.Id, tier.Id, new TierSpec("Regular", null, 900)).IsFailure);

        // Started phase: price and start locked, end can still move (not into the past).
        Assert.Equal(
            "Events.PhaseStarted",
            @event.UpdatePhase(date.Id, tier.Id, phase.Id, Phase("Early Bird", 999, _now, _now.AddDays(7)), later).Error!.Key);
        Assert.False(
            @event.UpdatePhase(date.Id, tier.Id, phase.Id, Phase("Early Bird", 1500, _now, _now.AddDays(3)), later).IsFailure);
        Assert.Equal(
            "Events.PhaseStarted",
            @event.RemovePhase(date.Id, tier.Id, phase.Id, later).Error!.Key);

        // Withdraw instead of delete.
        Assert.False(@event.WithdrawTier(date.Id, tier.Id).IsFailure);
        Assert.Equal(SaleItemStatus.Withdrawn, tier.Status);
    }

    [Fact]
    public void CancelDateKeepsAtLeastOneDate()
    {
        var @event = ReadyEvent(out var day1, out _);
        var day2 = AddDate(@event, _night.AddDays(1)).Value!;
        var tier2 = AddTier(@event, day2, "Regular", 100).Value!;
        Assert.False(AddPhase(@event, day2, tier2, "General", 1000, _now, _night.AddDays(1)).IsFailure);
        Assert.False(@event.Publish(_now).IsFailure);

        Assert.False(@event.CancelDate(day1.Id, _now).IsFailure);
        Assert.Equal("Events.LastDate", @event.CancelDate(day2.Id, _now).Error!.Key);
    }

    [Fact]
    public void CancelledEventIsReadOnlyAndCompleteWaitsForTheLastDate()
    {
        var published = ReadyEvent(out var date, out _);
        Assert.False(published.Publish(_now).IsFailure);

        Assert.Equal("Events.NotFinished", published.Complete(_now).Error!.Key);
        Assert.False(published.Complete(date.EndsAt).IsFailure);
        Assert.Equal(EventStatus.Completed, published.Status);

        var cancelled = ReadyEvent(out _, out _);
        Assert.Equal("Events.CancellationReasonRequired", cancelled.Cancel(" ", _now).Error!.Key);
        Assert.False(cancelled.Cancel("Venue flooded", _now).IsFailure);
        Assert.Equal("Events.EventClosed", AddDate(cancelled, _night.AddDays(3)).Error!.Key);
    }

    [Fact]
    public void LibraryVenueIsCopiedAndEditingTheCopyLeavesTheLibraryAlone()
    {
        var venue = Venue.Create(new VenueId(Guid.CreateVersion7()), Details("Uhuru Gardens"), 5000).Value!;
        Assert.False(venue.AddEntrance(new VenueEntranceId(Guid.CreateVersion7()), "Main gate", null).IsFailure);

        var @event = NewEvent();
        Assert.False(@event.UseVenue(venue, () => new EventEntranceId(Guid.CreateVersion7())).IsFailure);

        Assert.Equal(venue.Id, @event.Venue!.SourceVenueId);
        Assert.Equal("Main gate", Assert.Single(@event.Entrances).Name);
        Assert.NotEqual(venue.Entrances[0].Id.Value, @event.Entrances[0].Id.Value);

        Assert.False(@event.UpdateVenueDetails(Details("Uhuru Gardens (North)")).IsFailure);
        Assert.Equal("Uhuru Gardens (North)", @event.Venue.Details.Name);
        Assert.Equal("Uhuru Gardens", venue.Details.Name);
    }

    [Fact]
    public void ArchivedVenueCannotBeUsed()
    {
        var venue = Venue.Create(new VenueId(Guid.CreateVersion7()), Details("Old hall"), null).Value!;
        Assert.False(venue.Archive().IsFailure);

        Assert.Equal(
            "Events.VenueArchived",
            NewEvent().UseVenue(venue, () => new EventEntranceId(Guid.CreateVersion7())).Error!.Key);
    }

    [Theory]
    [InlineData(1, 10, true, 1)]
    [InlineData(61, 10, true, 1)]
    [InlineData(10, 0, true, 1)]
    [InlineData(10, 10, true, 0)]
    public void PoliciesAreBounded(int holdMinutes, int maxPerOrder, bool transfers, int maxTransfers) =>
        Assert.True(EventPolicies.Create(false, transfers, maxTransfers, false, holdMinutes, maxPerOrder).IsFailure);

    [Fact]
    public void EveryChangeRaisesAnEventAndBumpsTheVersion()
    {
        var @event = NewEvent();
        var before = @event.Version;
        var date = AddDate(@event, _night).Value!;
        AddTier(@event, date, "Regular", 10);

        Assert.Equal(before + 2, @event.Version);
        Assert.Contains(
            @event.Events.OfType<EventStructureChanged>(),
            x => x is { Part: EventPart.Tier, Change: ChangeKind.Added });
    }

    // -------------------------------------------------------------------------------------------------------------

    private static Event NewEvent() =>
        Event.Create(new EventId(Guid.CreateVersion7()), "krafthaus-001", "KraftHaus 001", null, null, null).Value!;

    private static Event ReadyEvent(out EventDate date, out TicketTier tier)
    {
        var @event = NewEvent();
        Assert.False(@event.UseInlineVenue(Details("KICC")).IsFailure);
        date = AddDate(@event, _night).Value!;
        tier = AddTier(@event, date, "Regular", 500).Value!;
        Assert.False(AddPhase(@event, date, tier, "Early Bird", 1500, _now, _now.AddDays(7)).IsFailure);
        return @event;
    }

    private static VenueDetails Details(string name) =>
        VenueDetails.Create(name, "Langata Road", "Nairobi", null, null).Value!;

    private static Result<EventDate> AddDate(Event @event, DateTimeOffset startsAt) =>
        @event.AddDate(
            new EventDateId(Guid.CreateVersion7()),
            new DateSpec(null, startsAt, startsAt.AddHours(8), null),
            _now);

    private static Result<TicketTier> AddTier(
        Event @event,
        EventDate date,
        string name,
        int capacity) =>
        @event.AddTier(date.Id, new TicketTierId(Guid.CreateVersion7()), new TierSpec(name, null, capacity));

    private static PhaseSpec Phase(string name, long price, DateTimeOffset startsAt, DateTimeOffset endsAt) =>
        new(name, Money.KesWhole(price), startsAt, endsAt, null);

    private static Result<SalePhase> AddPhase(
        Event @event,
        EventDate date,
        TicketTier tier,
        string name,
        long price,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt) =>
        @event.AddPhase(
            date.Id,
            tier.Id,
            new SalePhaseId(Guid.CreateVersion7()),
            Phase(name, price, startsAt, endsAt),
            _now);

    private static Result<GroupOffer> AddOffer(
        Event @event,
        EventDate date,
        TicketTier tier,
        GroupOfferKind kind,
        string name,
        int admits) =>
        @event.AddGroupOffer(
            date.Id,
            tier.Id,
            new GroupOfferId(Guid.CreateVersion7()),
            new OfferSpec(kind, name, admits, Money.KesWhole(2800), _now, _night, null),
            _now);
}
