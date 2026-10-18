using System.Globalization;
using Common.Application.Auditing;
using Common.Domain.Aggregates;
using Common.Domain.Events;
using Content.Domain;

namespace Content.Infrastructure.Auditing;

/// <summary>Content domain events → audit actions (<c>content.article.*</c>, <c>content.site.*</c>).</summary>
public static class ContentAuditMapper
{
    public const string Module = "content";

    public static (string Action, AuditTarget Target, IReadOnlyDictionary<string, string> Metadata) Map(
        DomainEvent domainEvent,
        IAggregateRoot aggregate)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(aggregate);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        var (action, targetType) = domainEvent switch
        {
            ArticleCreated e => (Article("content.article.created", e.Slug), "article"),
            ArticleUpdated e => (Article("content.article.updated", e.Slug), "article"),
            ArticlePublished e => (Article("content.article.published", e.Slug), "article"),
            ArticleUnpublished e => (Article("content.article.unpublished", e.Slug), "article"),
            ArticleArchived e => (Article("content.article.archived", e.Slug), "article"),
            ArticleCoverReplaced e => (Article("content.article.cover_replaced", e.Slug), "article"),
            SiteBlockDraftSaved e => (Block("content.site.draft_saved", e.Key), "site_block"),
            SiteBlockPublished e => (Block("content.site.published", e.Key), "site_block"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(domainEvent),
                domainEvent.GetType().Name,
                "Every Content domain event needs a stable audit action.")
        };

        return (action, new AuditTarget(targetType, aggregate.Id.Value.ToString("D", CultureInfo.InvariantCulture)), metadata);

        string Article(string name, string slug)
        {
            metadata["slug"] = slug;
            return name;
        }

        string Block(string name, SiteBlockKey key)
        {
            metadata["block"] = key.ToString();
            return name;
        }
    }
}
