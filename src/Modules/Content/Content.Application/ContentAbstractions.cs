using System.Text.RegularExpressions;
using Common.Domain.ResultMonad;
using Content.Domain;
using Microsoft.Extensions.Options;

namespace Content.Application;

public interface IArticleRepository
{
    void Add(Article article);

    Task<Article?> FindAsync(ArticleId id, CancellationToken cancellationToken);

    Task<Article?> FindPublishedBySlugAsync(string slug, CancellationToken cancellationToken);

    Task<bool> SlugTakenAsync(string slug, ArticleId? except, CancellationToken cancellationToken);

    Task<IReadOnlyList<Article>> ListAsync(ArticleStatus? status, CancellationToken cancellationToken);

    /// <summary>Published articles, newest first, optionally with a tag, before a publication time (paging).</summary>
    Task<IReadOnlyList<Article>> ListPublishedAsync(string? tag, DateTimeOffset? before, int limit, CancellationToken cancellationToken);
}

public interface ISiteBlockRepository
{
    void Add(SiteBlock block);

    Task<IReadOnlyList<SiteBlock>> ListAsync(CancellationToken cancellationToken);

    Task<SiteBlock?> FindAsync(SiteBlockKey key, CancellationToken cancellationToken);
}

public interface IContentUnitOfWork
{
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Article covers on disk, one folder per tenant. File names come from ids only.</summary>
public interface IContentImageStorage
{
    Task<string> SaveAsync(Guid ownerId, int version, string extension, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    Stream? OpenRead(string fileName);

    void Delete(string fileName);
}

/// <summary>Public URLs for covers (served by <c>GET /api/public/content-images/{file}</c>).</summary>
public static partial class ContentImageUrls
{
    public const string Route = "/api/public/content-images/";

    public static string? For(string? fileName) => fileName is null ? null : Route + fileName;

    public static bool IsValidName(string fileName) => fileName.Length < 64 && StoredName().IsMatch(fileName);

    [GeneratedRegex("^[0-9a-f]{32}-[0-9]{1,6}\\.(jpg|png|webp)$", RegexOptions.None, 50)]
    private static partial Regex StoredName();
}

/// <summary><c>Content</c>: where covers are kept and how big they may be.</summary>
public sealed class ContentOptions
{
    public const string SectionName = "Content";

    /// <summary>Relative to the API folder, or absolute. Keep it outside the site folder so deploys don't touch it.</summary>
    public string ImagesRootPath { get; init; } = "App_Data/content-images";

    public int MaxImageBytes { get; init; } = 3 * 1024 * 1024;

    public int MaxPageSize { get; init; } = 50;
}

public sealed class ContentOptionsValidator : IValidateOptions<ContentOptions>
{
    public ValidateOptionsResult Validate(string? name, ContentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ImagesRootPath))
        {
            failures.Add("Content:ImagesRootPath is required.");
        }

        if (options.MaxImageBytes is < 50 * 1024 or > 20 * 1024 * 1024)
        {
            failures.Add("Content:MaxImageBytes must be 51200-20971520.");
        }

        if (options.MaxPageSize is < 5 or > 200)
        {
            failures.Add("Content:MaxPageSize must be 5-200.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
