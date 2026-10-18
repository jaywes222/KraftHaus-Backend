using System.Text.RegularExpressions;
using Common.Domain.Aggregates;
using Common.Domain.Events;
using Common.Domain.ResultMonad;

namespace Content.Domain;

public enum ArticleStatus
{
    Draft = 1,
    Published = 2,
    Archived = 3
}

/// <summary>
///     A news or story article on the public site (UF-024). The body is Markdown, stored as written; the client renders
///     it without raw HTML. Draft → Published (and back to Draft to take it down) → Archived (kept, never deleted).
/// </summary>
public sealed partial class Article : AggregateRoot<ArticleId>
{
    public const int SlugMaxLength = 80;
    public const int TitleMaxLength = 120;
    public const int SummaryMaxLength = 280;
    public const int BodyMaxLength = 50_000;
    public const int AuthorMaxLength = 80;
    public const int MaxTags = 8;
    public const int TagMaxLength = 24;
    public const int CoverMaxLength = 64;

    private Article(ArticleId id)
        : base(id)
    {
    }

    /// <summary>Lower-case words and hyphens; the public address (<c>/stories/{slug}</c>).</summary>
    public string Slug { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string? Summary { get; private set; }

    public string Body { get; private set; } = string.Empty;

    /// <summary>The byline shown on the article (staff choose it; not the account).</summary>
    public string? AuthorName { get; private set; }

    /// <summary>Stored as <c>,a,b,</c> so a tag can be matched with a simple <c>LIKE '%,tag,%'</c>.</summary>
    private string _tags = string.Empty;

    public IReadOnlyList<string> Tags => _tags.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>How a tag sits inside the stored list (for queries).</summary>
    public static string TagToken(string tag) => $",{tag},";

    /// <summary>Stored cover image file name, or <c>null</c>.</summary>
    public string? CoverImage { get; private set; }

    public int CoverVersion { get; private set; }

    public ArticleStatus Status { get; private set; }

    /// <summary>First publication; kept when an article is taken down and published again.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    public static Result<Article> Create(ArticleId id, ArticleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var valid = Validate(input);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var article = new Article(id) { Status = ArticleStatus.Draft };
        article.Apply(valid.Value!);
        article.RaiseEvent(new ArticleCreated(id, article.Slug));
        return article;
    }

    public Result Update(ArticleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (Status == ArticleStatus.Archived)
        {
            return ContentErrors.Archived;
        }

        var valid = Validate(input);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var @event = new ArticleUpdated(Id, valid.Value!.Slug!);
        Apply(valid.Value);
        RaiseEvent(@event);
        return Result.Success;
    }

    public Result Publish(DateTimeOffset now)
    {
        if (Status == ArticleStatus.Archived)
        {
            return ContentErrors.Archived;
        }

        if (Status == ArticleStatus.Published)
        {
            return Result.Success;
        }

        var @event = new ArticlePublished(Id, Slug);
        Status = ArticleStatus.Published;
        PublishedAt ??= now;
        RaiseEvent(@event);
        return Result.Success;
    }

    public Result Unpublish()
    {
        if (Status != ArticleStatus.Published)
        {
            return Result.Success;
        }

        var @event = new ArticleUnpublished(Id, Slug);
        Status = ArticleStatus.Draft;
        RaiseEvent(@event);
        return Result.Success;
    }

    public void Archive()
    {
        if (Status == ArticleStatus.Archived)
        {
            return;
        }

        var @event = new ArticleArchived(Id, Slug);
        Status = ArticleStatus.Archived;
        RaiseEvent(@event);
    }

    /// <returns>The previous cover file (to delete once the change is saved).</returns>
    public Result<string?> ReplaceCover(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (Status == ArticleStatus.Archived)
        {
            return ContentErrors.Archived;
        }

        var previous = CoverImage;
        var @event = new ArticleCoverReplaced(Id, Slug);

        CoverImage = fileName;
        CoverVersion++;

        RaiseEvent(@event);
        return Result<string?>.Success(previous);
    }

    public static bool IsValidSlug(string? slug) =>
        slug is { Length: > 0 and <= SlugMaxLength } && SlugPattern().IsMatch(slug);

    private void Apply(ArticleInput input)
    {
        Slug = input.Slug!;
        Title = input.Title!;
        Summary = input.Summary;
        Body = input.Body!;
        AuthorName = input.AuthorName;
        var tags = input.Tags ?? [];
        _tags = tags.Count == 0 ? string.Empty : $",{string.Join(',', tags)},";
    }

    private static Result<ArticleInput> Validate(ArticleInput input)
    {
        var errors = new List<string>();

        // Addresses and tags are lower-case by rule (they appear in URLs), not a culture-sensitive comparison.
#pragma warning disable CA1308
        var slug = input.Slug?.Trim().ToLowerInvariant();
        var title = input.Title?.Trim();
        var summary = string.IsNullOrWhiteSpace(input.Summary) ? null : input.Summary.Trim();
        var body = input.Body?.Trim() ?? string.Empty;
        var author = string.IsNullOrWhiteSpace(input.AuthorName) ? null : input.AuthorName.Trim();
        var tags = (input.Tags ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
#pragma warning restore CA1308
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!IsValidSlug(slug))
        {
            errors.Add($"Slug: Use lower-case letters, numbers and single hyphens, up to {SlugMaxLength} characters.");
        }

        if (title is not { Length: > 0 and <= TitleMaxLength })
        {
            errors.Add($"Title: Give it a title of up to {TitleMaxLength} characters.");
        }

        if (summary is { Length: > SummaryMaxLength })
        {
            errors.Add($"Summary: Keep it to {SummaryMaxLength} characters.");
        }

        if (body.Length is 0 or > BodyMaxLength)
        {
            errors.Add($"Body: Write the article (up to {BodyMaxLength} characters).");
        }

        if (author is { Length: > AuthorMaxLength })
        {
            errors.Add($"AuthorName: Keep it to {AuthorMaxLength} characters.");
        }

        if (tags.Count > MaxTags || tags.Any(x => x.Length > TagMaxLength || x.Contains(',', StringComparison.Ordinal)))
        {
            errors.Add($"Tags: Up to {MaxTags} tags of up to {TagMaxLength} characters.");
        }

        return errors.Count > 0
            ? ContentErrors.Invalid("ArticleInvalid", errors)
            : new ArticleInput(slug, title, summary, body, author, tags);
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SlugPattern();
}

public sealed record ArticleInput(string? Slug, string? Title, string? Summary, string? Body, string? AuthorName, IReadOnlyList<string>? Tags);

public sealed record ArticleCreated(ArticleId ArticleId, string Slug) : DomainEvent;

public sealed record ArticleUpdated(ArticleId ArticleId, string Slug) : DomainEvent;

public sealed record ArticlePublished(ArticleId ArticleId, string Slug) : DomainEvent;

public sealed record ArticleUnpublished(ArticleId ArticleId, string Slug) : DomainEvent;

public sealed record ArticleArchived(ArticleId ArticleId, string Slug) : DomainEvent;

public sealed record ArticleCoverReplaced(ArticleId ArticleId, string Slug) : DomainEvent;
