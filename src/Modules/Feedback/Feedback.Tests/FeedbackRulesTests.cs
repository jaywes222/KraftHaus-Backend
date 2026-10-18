using Feedback.Application;
using Feedback.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace Feedback.Tests;

public sealed class FeedbackRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 8);

    private static Rating Rate(int overall, int? music = null, string? comment = null) =>
        Rating.Submit(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64),
            new RatingInput(overall, music, null, null, null, comment), Today).Value!;

    [Fact]
    public void AnOverallScoreIsRequiredAndEveryScoreIsOneToFive()
    {
        var result = Rating.Submit(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64),
            new RatingInput(null, 6, 0, null, null, new string('x', 501)), Today);

        Assert.True(result.IsFailure);
        Assert.Equal("Feedback.RatingInvalid", result.Error!.Key);
        Assert.Equal(4, result.Error.SubErrors!.Count);
    }

    [Fact]
    public void ChangingTheCommentUnhidesItButScoresAlone()
    {
        var rating = Rate(4, comment: "Loved the DJ");
        rating.SetCommentHidden(true);

        rating.Change(new RatingInput(5, null, null, null, null, "Loved the DJ"), Today);
        Assert.True(rating.CommentHidden);

        rating.Change(new RatingInput(5, null, null, null, null, "Loved the DJ and the food"), Today);
        Assert.False(rating.CommentHidden);

        var noComment = Rate(3);
        noComment.SetCommentHidden(true);
        Assert.False(noComment.CommentHidden);
    }

    [Fact]
    public void SummariesCountAveragesAndTheDistribution()
    {
        var summary = FeedbackService.Summarise([Rate(5, 4), Rate(4), Rate(4, 2), Rate(1)]);

        Assert.Equal(4, summary.Ratings);
        Assert.Equal(3.5m, summary.Average);
        Assert.Equal([1, 0, 0, 2, 1], summary.Distribution);
        Assert.Equal(3m, summary.AspectAverages["Music"]);
        Assert.False(summary.AspectAverages.ContainsKey("Food"));

        Assert.Null(FeedbackService.Summarise([]).Average);
    }

    [Fact]
    public void TheHashIsStablePerTicketAndDateAndDependsOnTheSecret()
    {
        var ticket = Guid.CreateVersion7();
        var date = Guid.CreateVersion7();
        var hasher = new RatingHasher(Options.Create(new FeedbackOptions { DedupeSecret = new string('s', 40) }));
        var other = new RatingHasher(Options.Create(new FeedbackOptions { DedupeSecret = new string('t', 40) }));

        Assert.Equal(hasher.Hash(ticket, date), hasher.Hash(ticket, date));
        Assert.Equal(64, hasher.Hash(ticket, date).Length);
        Assert.NotEqual(hasher.Hash(ticket, date), hasher.Hash(ticket, Guid.CreateVersion7()));
        Assert.NotEqual(hasher.Hash(ticket, date), other.Hash(ticket, date));
        Assert.DoesNotContain(ticket.ToString("N"), hasher.Hash(ticket, date), StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionNeedsItsOwnSecret()
    {
        var production = new FeedbackOptionsValidator(new Environment("Production"));

        Assert.True(production.Validate(null, new FeedbackOptions()).Failed);
        Assert.True(production.Validate(null, new FeedbackOptions { DedupeSecret = FeedbackOptions.DevelopmentSecret }).Failed);
        Assert.True(production.Validate(null, new FeedbackOptions { DedupeSecret = new string('k', 48) }).Succeeded);
        Assert.True(new FeedbackOptionsValidator(new Environment("Development")).Validate(null, new FeedbackOptions()).Succeeded);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
