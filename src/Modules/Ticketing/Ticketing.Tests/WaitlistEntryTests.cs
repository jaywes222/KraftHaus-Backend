using Ticketing.Domain.Common;
using Ticketing.Domain.Waitlist;
using Xunit;

namespace Ticketing.Tests;

public sealed class WaitlistEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid EventId = Guid.CreateVersion7();

    internal static WaitlistApplicant Valid() =>
        new(
            Name: "  Wanjiru Kamau ",
            Phone: "0712 345 678",
            Age: 27,
            Gender: WaitlistGender.Woman,
            Location: "Kilimani",
            Email: null,
            PrivacyAccepted: true,
            PrivacyPolicyVersion: "2026-10-01",
            MarketingConsent: false);

    [Fact]
    public void JoinNormalisesAndStampsConsent()
    {
        var entry = WaitlistEntry.Join(NewId(), EventId, Valid() with { MarketingConsent = true }, 18, Now).Value!;

        Assert.Equal("Wanjiru Kamau", entry.Name);
        Assert.Equal("+254712345678", entry.Phone);
        Assert.Equal(EventId, entry.EventId);
        Assert.Equal("2026-10-01", entry.PrivacyPolicyVersion);
        Assert.Equal(Now, entry.PrivacyAcceptedAt);
        Assert.Equal(Now, entry.JoinedAt);
        Assert.True(entry.MarketingConsent);
        Assert.Null(entry.ConfirmationSentAt);

        var joined = Assert.IsType<WaitlistJoined>(Assert.Single(entry.Events));
        Assert.True(joined.MarketingConsent);
        Assert.Equal(EventId, joined.EventId);
    }

    [Fact]
    public void EveryInvalidFieldIsReportedAtOnceWithItsFieldPrefix()
    {
        var result = WaitlistEntry.Join(
            NewId(),
            EventId,
            new WaitlistApplicant("A", "12345", 12, (WaitlistGender)0, "", "not-an-email", false, null, false),
            18,
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Ticketing.WaitlistInvalid", result.Error!.Key);

        var fields = result.Error.SubErrors!.Select(x => x[..x.IndexOf(':', StringComparison.Ordinal)]).ToList();
        Assert.Equal(
            ["Name", "Phone", "Age", "Gender", "Location", "Email", "PrivacyAccepted", "PrivacyPolicyVersion"],
            fields);
    }

    [Theory]
    [InlineData(17, false)]
    [InlineData(18, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void AgeHonoursTheConfiguredMinimumAndTheCap(int age, bool accepted)
    {
        var result = WaitlistEntry.Join(NewId(), EventId, Valid() with { Age = age }, 18, Now);

        Assert.Equal(accepted, !result.IsFailure);
    }

    [Theory]
    [InlineData("wanjiru@example.com", true)]
    [InlineData("  wanjiru@example.co.ke ", true)]
    [InlineData("wanjiru@example", false)]
    [InlineData("wanjiru@@example.com", false)]
    [InlineData("wan jiru@example.com", false)]
    [InlineData("@example.com", false)]
    public void EmailIsOptionalButCheckedWhenPresent(string email, bool accepted)
    {
        var result = WaitlistEntry.Join(NewId(), EventId, Valid() with { Email = email }, 18, Now);

        Assert.Equal(accepted, !result.IsFailure);
    }

    [Fact]
    public void NamesWithControlCharactersAreRejected()
    {
        var result = WaitlistEntry.Join(NewId(), EventId, Valid() with { Name = "Wanjiru\u0000Kamau" }, 18, Now);

        Assert.True(result.IsFailure);
        Assert.StartsWith("Name:", Assert.Single(result.Error!.SubErrors!), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmationIsRecordedOnce()
    {
        var entry = WaitlistEntry.Join(NewId(), EventId, Valid(), 18, Now).Value!;
        entry.ClearEvents();

        entry.MarkConfirmationSent(Now.AddSeconds(2));
        entry.MarkConfirmationSent(Now.AddSeconds(9));

        Assert.Equal(Now.AddSeconds(2), entry.ConfirmationSentAt);
        Assert.IsType<WaitlistConfirmationSent>(Assert.Single(entry.Events));
    }

    private static WaitlistEntryId NewId() => new(Guid.CreateVersion7());
}
