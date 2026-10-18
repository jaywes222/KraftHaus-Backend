using Commerce.Domain.Common;
using Commerce.Domain.Krafts;
using Xunit;

namespace Commerce.Tests;

public sealed class KraftTests
{
    private static readonly Guid Event = Guid.CreateVersion7();

    [Fact]
    public void CreatingAKraftChecksEveryFieldAtOnce()
    {
        var result = Kraft.Create(NewId(), " ", (KraftDiscipline)99, new string('x', 700), "slug");

        Assert.True(result.IsFailure);
        Assert.Equal("Commerce.KraftInvalid", result.Error!.Key);
        Assert.Equal(3, result.Error.SubErrors!.Count);
    }

    [Fact]
    public void BrandNamesAreUniqueWithinAKraft()
    {
        var kraft = NewKraft();

        Assert.False(kraft.AddBrand(new KraftBrandId(Guid.CreateVersion7()), "Nyama Mama", null).IsFailure);
        var duplicate = kraft.AddBrand(new KraftBrandId(Guid.CreateVersion7()), "nyama mama", null);

        Assert.Equal("Commerce.BrandExists", duplicate.Error!.Key);
    }

    [Fact]
    public void KrafterInstagramHandleLosesItsAt()
    {
        var kraft = NewKraft();

        var krafter = kraft.AddKrafter(new KrafterId(Guid.CreateVersion7()), new KrafterSpec("Wanjiru K", "Head chef", null, "@wanjiru.cooks"));

        Assert.Equal("wanjiru.cooks", krafter.Value!.Instagram);
        Assert.Equal("Commerce.KrafterInvalid",
            kraft.AddKrafter(new KrafterId(Guid.CreateVersion7()), new KrafterSpec("Wanjiru K", null, null, "not a handle")).Error!.Key);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(12.345)]
    public void CommissionMustBeAPercentageWithTwoDecimals(double commission)
    {
        var kraft = NewKraft();

        var result = kraft.SetEventTerms(new KraftParticipationId(Guid.CreateVersion7()), Event, (decimal)commission, null);

        Assert.Equal("Commerce.KraftEventInvalid", result.Error!.Key);
    }

    [Fact]
    public void SettingTermsTwiceChangesTheExistingParticipation()
    {
        var kraft = NewKraft();

        kraft.SetEventTerms(new KraftParticipationId(Guid.CreateVersion7()), Event, 10m, "Stall 4");
        kraft.SetEventTerms(new KraftParticipationId(Guid.CreateVersion7()), Event, 12.5m, "Stall 5");

        var terms = Assert.Single(kraft.Participations);
        Assert.Equal(12.5m, terms.CommissionPercent);
        Assert.Equal("Stall 5", terms.Stall);
        Assert.Contains(kraft.Events, x => x is KraftEventTermsChanged { CommissionChanged: true });
    }

    [Fact]
    public void AnArchivedKraftIsNotAtAnyEventAndCannotChange()
    {
        var kraft = NewKraft();
        kraft.SetEventTerms(new KraftParticipationId(Guid.CreateVersion7()), Event, 10m, null);

        Assert.False(kraft.Archive().IsFailure);

        Assert.Null(kraft.AtEvent(Event));
        Assert.Equal("Commerce.KraftArchived", kraft.UpdateProfile("New", KraftDiscipline.Food, null).Error!.Key);
        Assert.Equal("Commerce.KraftArchived", kraft.Archive().Error!.Key);
    }

    [Theory]
    [InlineData("Mama Pima's Kitchen!", "mama-pima-s-kitchen")]
    [InlineData("  Café   Ole ", "cafe-ole")]
    [InlineData("***", "kraft")]
    public void SlugsAreLowerCaseAsciiWithSingleDashes(string name, string expected) =>
        Assert.Equal(expected, Slug.From(name));

    private static Kraft NewKraft() =>
        Kraft.Create(NewId(), "Mama Pima", KraftDiscipline.Food, null, "mama-pima").Value!;

    private static KraftId NewId() => new(Guid.CreateVersion7());
}
