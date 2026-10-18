using Common.Domain.ResultMonad;
using Content.Application;
using Content.Domain;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;

namespace Content.Infrastructure.Persistence;

internal sealed class ArticleRepository(ContentDbContext db) : IArticleRepository
{
    public void Add(Article article) => db.Articles.Add(article);

    public Task<Article?> FindAsync(ArticleId id, CancellationToken cancellationToken) =>
        db.Articles.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Article?> FindPublishedBySlugAsync(string slug, CancellationToken cancellationToken) =>
        db.Articles.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug && x.Status == ArticleStatus.Published, cancellationToken);

    public Task<bool> SlugTakenAsync(string slug, ArticleId? except, CancellationToken cancellationToken) =>
        db.Articles.AsNoTracking().AnyAsync(x => x.Slug == slug && (except == null || x.Id != except), cancellationToken);

    public async Task<IReadOnlyList<Article>> ListAsync(ArticleStatus? status, CancellationToken cancellationToken)
    {
        var query = db.Articles.AsNoTracking();
        query = status is { } s ? query.Where(x => x.Status == s) : query.Where(x => x.Status != ArticleStatus.Archived);

        return await query.OrderByDescending(x => x.CreatedOn).Take(500).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Article>> ListPublishedAsync(string? tag, DateTimeOffset? before, int limit, CancellationToken cancellationToken)
    {
        var query = db.Articles.AsNoTracking().Where(x => x.Status == ArticleStatus.Published);

        if (tag is not null)
        {
#pragma warning disable CA1308 // Tags are stored lower-case by rule.
            var token = Article.TagToken(tag.ToLowerInvariant());
#pragma warning restore CA1308
            query = query.Where(x => EF.Property<string>(x, "_tags").Contains(token));
        }

        if (before is { } b)
        {
            query = query.Where(x => x.PublishedAt < b);
        }

        return await query.OrderByDescending(x => x.PublishedAt).Take(limit).ToListAsync(cancellationToken);
    }
}

internal sealed class SiteBlockRepository(ContentDbContext db) : ISiteBlockRepository
{
    public void Add(SiteBlock block) => db.SiteBlocks.Add(block);

    public async Task<IReadOnlyList<SiteBlock>> ListAsync(CancellationToken cancellationToken) =>
        await db.SiteBlocks.ToListAsync(cancellationToken);

    public Task<SiteBlock?> FindAsync(SiteBlockKey key, CancellationToken cancellationToken) =>
        db.SiteBlocks.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
}

internal sealed class ContentUnitOfWork(ContentDbContext db) : IContentUnitOfWork
{
    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ContentErrors.ConcurrentUpdate;
        }
        catch (UniqueConstraintException ex) when (ex.ConstraintName == ContentDbContext.SlugIndex)
        {
            return ContentErrors.SlugTaken;
        }
        catch (UniqueConstraintException)
        {
            return ContentErrors.ConcurrentUpdate;
        }
    }
}
