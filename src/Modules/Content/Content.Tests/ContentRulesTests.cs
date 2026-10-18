using Common.Domain.Events;
using Content.Domain;
using Content.Infrastructure.Auditing;
using Xunit;

namespace Content.Tests;

public sealed class ContentRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static ArticleInput Input(string slug = "krafthaus-001-lineup", IReadOnlyList<string>? tags = null) =>
        new(slug, "The KRAFTHAUS 001 line-up", "Who's playing on 7 November.", "## Line-up\n\nDJ Kace, ...", "KRAFTHAUS", tags ?? ["Music", "music", " Events "]);

    [Fact]
    public void ArticlesNormaliseSlugsAndTags()
    {
        var article = Article.Create(new ArticleId(Guid.CreateVersion7()), Input(" KraftHaus-001-Lineup ")).Value!;

        Assert.Equal("krafthaus-001-lineup", article.Slug);
        Assert.Equal(["music", "events"], article.Tags);
        Assert.Equal(ArticleStatus.Draft, article.Status);
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("double--hyphen")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("emoji-🎶")]
    [InlineData("")]
    public void BadSlugsAreRefused(string slug) =>
        Assert.True(Article.Create(new ArticleId(Guid.CreateVersion7()), Input(slug)).IsFailure);

    [Fact]
    public void TooManyOrCommaTagsAreRefused()
    {
        Assert.True(Article.Create(new ArticleId(Guid.CreateVersion7()), Input(tags: [.. Enumerable.Range(1, 9).Select(i => $"t{i}")])).IsFailure);
        Assert.True(Article.Create(new ArticleId(Guid.CreateVersion7()), Input(tags: ["a,b"])).IsFailure);
    }

    [Fact]
    public void PublishingKeepsTheFirstDateAndArchivedArticlesAreFrozen()
    {
        var article = Article.Create(new ArticleId(Guid.CreateVersion7()), Input()).Value!;

        article.Publish(Now);
        article.Unpublish();
        article.Publish(Now.AddDays(1));
        Assert.Equal(Now, article.PublishedAt);

        article.Archive();
        Assert.True(article.Update(Input()).IsFailure);
        Assert.True(article.Publish(Now).IsFailure);
        Assert.True(article.ReplaceCover("abc-1.jpg").IsFailure);
    }

    [Fact]
    public void CoversReturnThePreviousFileToDelete()
    {
        var article = Article.Create(new ArticleId(Guid.CreateVersion7()), Input()).Value!;

        Assert.Null(article.ReplaceCover("a-1.jpg").Value);
        Assert.Equal("a-1.jpg", article.ReplaceCover("a-2.jpg").Value);
        Assert.Equal(2, article.CoverVersion);
    }

    [Fact]
    public void SiteBlocksTakeAJsonObjectAndPublishOnlyChanges()
    {
        var block = SiteBlock.Create(new SiteBlockId(Guid.CreateVersion7()), SiteBlockKey.Hero);

        Assert.True(block.SaveDraft("[1,2]").IsFailure);
        Assert.True(block.SaveDraft("not json").IsFailure);
        Assert.True(block.SaveDraft($"{{\"x\":\"{new string('a', SiteBlock.MaxContentLength)}\"}}").IsFailure);

        Assert.False(block.SaveDraft("""{ "headline": "Made Together",  "cta": "Join the waitlist" }""").IsFailure);
        Assert.Equal("""{"headline":"Made Together","cta":"Join the waitlist"}""", block.Draft);
        Assert.True(block.HasUnpublishedChanges);

        Assert.False(block.Publish(Now).IsFailure);
        Assert.False(block.HasUnpublishedChanges);
        Assert.Equal("Content.NothingToPublish", block.Publish(Now).Error!.Key);
    }

    [Fact]
    public void EveryContentEventHasAnAuditAction()
    {
        var article = Article.Create(new ArticleId(Guid.CreateVersion7()), Input()).Value!;
        var events = new DomainEvent[]
        {
            new ArticleCreated(article.Id, "a"), new ArticleUpdated(article.Id, "a"), new ArticlePublished(article.Id, "a"),
            new ArticleUnpublished(article.Id, "a"), new ArticleArchived(article.Id, "a"), new ArticleCoverReplaced(article.Id, "a"),
            new SiteBlockDraftSaved(new SiteBlockId(Guid.CreateVersion7()), SiteBlockKey.Faq),
            new SiteBlockPublished(new SiteBlockId(Guid.CreateVersion7()), SiteBlockKey.Faq)
        };

        var actions = events.Select(e => ContentAuditMapper.Map(e, article).Action).ToList();
        Assert.Equal(events.Length, actions.Distinct().Count());
    }
}
