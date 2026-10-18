using Common.Domain.ResultMonad;
using EntityFramework.Exceptions.Common;
using Feedback.Application;
using Feedback.Domain;
using Microsoft.EntityFrameworkCore;

namespace Feedback.Infrastructure.Persistence;

internal sealed class RatingRepository(FeedbackDbContext db) : IRatingRepository
{
    public Task<Rating?> FindByHashAsync(string dedupeHash, CancellationToken cancellationToken) =>
        db.Ratings.SingleOrDefaultAsync(x => x.DedupeHash == dedupeHash, cancellationToken);

    public Task<Rating?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Ratings.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Rating>> ListForEventAsync(Guid eventId, CancellationToken cancellationToken) =>
        await db.Ratings.AsNoTracking().Where(x => x.EventId == eventId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Rating>> ListCommentsAsync(Guid eventId, Guid? eventDateId, int limit, CancellationToken cancellationToken)
    {
        var query = db.Ratings.AsNoTracking().Where(x => x.EventId == eventId && x.Comment != null);
        if (eventDateId is { } dateId)
        {
            query = query.Where(x => x.EventDateId == dateId);
        }

        return await query.OrderByDescending(x => x.SubmittedOn).ThenByDescending(x => x.Id).Take(limit).ToListAsync(cancellationToken);
    }

    public void Add(Rating rating) => db.Ratings.Add(rating);
}

internal sealed class FeedbackUnitOfWork(FeedbackDbContext db) : IFeedbackUnitOfWork
{
    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException or UniqueConstraintException)
        {
            db.ChangeTracker.Clear();
            return FeedbackErrors.Concurrent;
        }
    }
}
