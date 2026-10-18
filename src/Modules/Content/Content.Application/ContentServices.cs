using System.Globalization;
using System.Text.Json;
using Common.Application.Files;
using Common.Domain.ResultMonad;
using Content.Domain;
using Microsoft.Extensions.Options;

namespace Content.Application;

/// <summary>Articles (UF-024): staff write, publish, take down and archive; visitors read what is published.</summary>
public sealed class ArticleService(
    IArticleRepository articles,
    IContentUnitOfWork unitOfWork,
    IContentImageStorage storage,
    IOptions<ContentOptions> options,
    TimeProvider time)
{
    // ---- Public ------------------------------------------------------------------------------------------------

    public async Task<Result<IReadOnlyList<ArticleSummaryView>>> ListPublishedAsync(string? tag, DateTimeOffset? before, int? limit, CancellationToken cancellationToken)
    {
        var cleanTag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();
        if (cleanTag is { Length: > Article.TagMaxLength })
        {
            return ContentErrors.Invalid("SearchInvalid", ["Tag: Unknown tag."]);
        }

        var list = await articles.ListPublishedAsync(cleanTag, before, Math.Clamp(limit ?? 12, 1, options.Value.MaxPageSize), cancellationToken);
        return Result<IReadOnlyList<ArticleSummaryView>>.Success([.. list.Select(ArticleSummaryView.From)]);
    }

    public async Task<Result<ArticleView>> GetPublishedAsync(string slug, CancellationToken cancellationToken) =>
        Article.IsValidSlug(slug) && await articles.FindPublishedBySlugAsync(slug, cancellationToken) is { } article
            ? ArticleView.From(article)
            : ContentErrors.ArticleNotFound;

    // ---- Admin -------------------------------------------------------------------------------------------------

    public async Task<Result<IReadOnlyList<ArticleSummaryView>>> ListAsync(ArticleStatus? status, CancellationToken cancellationToken)
    {
        var list = await articles.ListAsync(status, cancellationToken);
        return Result<IReadOnlyList<ArticleSummaryView>>.Success([.. list.Select(ArticleSummaryView.From)]);
    }

    public async Task<Result<ArticleView>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await articles.FindAsync(new ArticleId(id), cancellationToken) is { } article
            ? ArticleView.From(article)
            : ContentErrors.ArticleNotFound;

    public async Task<Result<ArticleView>> CreateAsync(ArticleInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var created = Article.Create(new ArticleId(Guid.CreateVersion7(time.GetUtcNow())), input);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var article = created.Value!;
        if (await articles.SlugTakenAsync(article.Slug, null, cancellationToken))
        {
            return ContentErrors.SlugTaken;
        }

        articles.Add(article);
        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : ArticleView.From(article);
    }

    public async Task<Result<ArticleView>> UpdateAsync(Guid id, ArticleInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var article = await articles.FindAsync(new ArticleId(id), cancellationToken);
        if (article is null)
        {
            return ContentErrors.ArticleNotFound;
        }

        var updated = article.Update(input);
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        if (await articles.SlugTakenAsync(article.Slug, article.Id, cancellationToken))
        {
            return ContentErrors.SlugTaken;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : ArticleView.From(article);
    }

    public Task<Result<ArticleView>> PublishAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, a => a.Publish(time.GetUtcNow()), cancellationToken);

    public Task<Result<ArticleView>> UnpublishAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, a => a.Unpublish(), cancellationToken);

    public Task<Result<ArticleView>> ArchiveAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, a =>
        {
            a.Archive();
            return Result.Success;
        }, cancellationToken);

    public async Task<Result<string>> UploadCoverAsync(Guid id, Stream content, long length, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var max = options.Value.MaxImageBytes;
        var tooBig = ContentErrors.Invalid("ImageInvalid", [$"File: Choose an image under {(max / (1024 * 1024)).ToString(CultureInfo.InvariantCulture)} MB."]);
        if (length <= 0 || length > max)
        {
            return tooBig;
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > max)
        {
            return tooBig;
        }

        var data = buffer.ToArray();
        if (ImageFormat.Sniff(data) is not { } format)
        {
            return ContentErrors.Invalid("ImageInvalid", ["File: Use a JPEG, PNG or WebP image."]);
        }

        var article = await articles.FindAsync(new ArticleId(id), cancellationToken);
        if (article is null)
        {
            return ContentErrors.ArticleNotFound;
        }

        var file = await storage.SaveAsync(article.Id.Value, article.CoverVersion + 1, format.Extension, data, cancellationToken);
        var replaced = article.ReplaceCover(file);
        if (replaced.IsFailure)
        {
            storage.Delete(file);
            return replaced.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            storage.Delete(file);
            return saved.Error!;
        }

        if (replaced.Value is { } previous && previous != file)
        {
            storage.Delete(previous);
        }

        return ContentImageUrls.For(file)!;
    }

    public (Stream Content, string ContentType)? OpenImage(string fileName) =>
        ContentImageUrls.IsValidName(fileName) && storage.OpenRead(fileName) is { } stream
            ? (stream, ImageFormat.ContentTypeOf(Path.GetExtension(fileName)))
            : null;

    private async Task<Result<ArticleView>> ChangeAsync(Guid id, Func<Article, Result> change, CancellationToken cancellationToken)
    {
        var article = await articles.FindAsync(new ArticleId(id), cancellationToken);
        if (article is null)
        {
            return ContentErrors.ArticleNotFound;
        }

        var changed = change(article);
        if (changed.IsFailure)
        {
            return changed.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : ArticleView.From(article);
    }
}

/// <summary>The Web Studio (UF-024): draft and publish the site's editable blocks.</summary>
public sealed class SiteStudioService(
    ISiteBlockRepository blocks,
    IContentUnitOfWork unitOfWork,
    TimeProvider time)
{
    /// <summary>What visitors see: every published block, by key.</summary>
    public async Task<Result<IReadOnlyDictionary<string, JsonElement>>> GetPublishedAsync(CancellationToken cancellationToken)
    {
        var list = await blocks.ListAsync(cancellationToken);
        var site = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var block in list.Where(x => x.Published is not null))
        {
            using var document = JsonDocument.Parse(block.Published!);
            site[JsonNamingPolicy.CamelCase.ConvertName(block.Key.ToString())] = document.RootElement.Clone();
        }

        return site;
    }

    public async Task<Result<IReadOnlyList<SiteBlockView>>> ListAsync(CancellationToken cancellationToken)
    {
        var existing = (await blocks.ListAsync(cancellationToken)).ToDictionary(x => x.Key);

        return Result<IReadOnlyList<SiteBlockView>>.Success(
        [
            .. Enum.GetValues<SiteBlockKey>().Select(key =>
                existing.TryGetValue(key, out var block) ? SiteBlockView.From(block) : SiteBlockView.Empty(key))
        ]);
    }

    public async Task<Result<SiteBlockView>> SaveDraftAsync(SiteBlockKey key, JsonElement content, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(key))
        {
            return ContentErrors.NotFound("BlockNotFound", "There is no such block.");
        }

        var block = await blocks.FindAsync(key, cancellationToken);
        if (block is null)
        {
            block = SiteBlock.Create(new SiteBlockId(Guid.CreateVersion7(time.GetUtcNow())), key);
            blocks.Add(block);
        }

        var saved = block.SaveDraft(content.GetRawText());
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        var result = await unitOfWork.SaveChangesAsync(cancellationToken);
        return result.IsFailure ? result.Error! : SiteBlockView.From(block);
    }

    public async Task<Result<SiteBlockView>> PublishAsync(SiteBlockKey key, CancellationToken cancellationToken)
    {
        var block = Enum.IsDefined(key) ? await blocks.FindAsync(key, cancellationToken) : null;
        if (block is null)
        {
            return ContentErrors.NothingToPublish;
        }

        var published = block.Publish(time.GetUtcNow());
        if (published.IsFailure)
        {
            return published.Error!;
        }

        var result = await unitOfWork.SaveChangesAsync(cancellationToken);
        return result.IsFailure ? result.Error! : SiteBlockView.From(block);
    }

    /// <summary>Publishes every block with draft changes in one save (the Studio's "Publish site").</summary>
    public async Task<Result<IReadOnlyList<SiteBlockView>>> PublishAllAsync(CancellationToken cancellationToken)
    {
        var list = await blocks.ListAsync(cancellationToken);
        var changed = list.Where(x => x.HasUnpublishedChanges).ToList();
        if (changed.Count == 0)
        {
            return ContentErrors.NothingToPublish;
        }

        var now = time.GetUtcNow();
        foreach (var block in changed)
        {
            block.Publish(now);
        }

        var result = await unitOfWork.SaveChangesAsync(cancellationToken);
        return result.IsFailure ? result.Error! : Result<IReadOnlyList<SiteBlockView>>.Success([.. changed.Select(SiteBlockView.From)]);
    }
}

// ---- Views -------------------------------------------------------------------------------------------------------

public sealed record ArticleSummaryView(
    Guid Id,
    string Slug,
    string Title,
    string? Summary,
    string? AuthorName,
    IReadOnlyList<string> Tags,
    string? CoverHref,
    ArticleStatus Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? LastModifiedOn)
{
    public static ArticleSummaryView From(Article article)
    {
        ArgumentNullException.ThrowIfNull(article);
        return new ArticleSummaryView(article.Id.Value, article.Slug, article.Title, article.Summary, article.AuthorName, article.Tags,
            ContentImageUrls.For(article.CoverImage), article.Status, article.PublishedAt, article.LastModifiedOn ?? article.CreatedOn);
    }
}

public sealed record ArticleView(
    Guid Id,
    string Slug,
    string Title,
    string? Summary,
    string Body,
    string? AuthorName,
    IReadOnlyList<string> Tags,
    string? CoverHref,
    ArticleStatus Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? LastModifiedOn)
{
    public static ArticleView From(Article article)
    {
        ArgumentNullException.ThrowIfNull(article);
        return new ArticleView(article.Id.Value, article.Slug, article.Title, article.Summary, article.Body, article.AuthorName, article.Tags,
            ContentImageUrls.For(article.CoverImage), article.Status, article.PublishedAt, article.LastModifiedOn ?? article.CreatedOn);
    }
}

public sealed record SiteBlockView(SiteBlockKey Key, JsonElement Draft, JsonElement? Published, bool HasUnpublishedChanges, DateTimeOffset? PublishedAt)
{
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    public static SiteBlockView From(SiteBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return new SiteBlockView(block.Key, Parse(block.Draft), block.Published is null ? null : Parse(block.Published), block.HasUnpublishedChanges, block.PublishedAt);
    }

    public static SiteBlockView Empty(SiteBlockKey key) => new(key, EmptyObject, null, false, null);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed record ArticleRequest(string? Slug, string? Title, string? Summary, string? Body, string? AuthorName, IReadOnlyList<string>? Tags)
{
    public ArticleInput ToInput() => new(Slug, Title, Summary, Body, AuthorName, Tags);
}

public sealed record SiteBlockRequest(JsonElement Content);
