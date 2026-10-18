using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Common.Application.Analytics;
using Common.Application.Customers;
using Common.Application.EventCatalog;
using Common.Application.Ticketing;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Common.Domain.TimeProvision;
using Feedback.Domain;
using Microsoft.Extensions.Options;

namespace Feedback.Application;

public interface IRatingRepository
{
    Task<Rating?> FindByHashAsync(string dedupeHash, CancellationToken cancellationToken);

    Task<Rating?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Rating>> ListForEventAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>Ratings with a comment, newest first.</summary>
    Task<IReadOnlyList<Rating>> ListCommentsAsync(Guid eventId, Guid? eventDateId, int limit, CancellationToken cancellationToken);

    void Add(Rating rating);
}

public interface IFeedbackUnitOfWork
{
    /// <summary>A lost race (same guest, two tabs) comes back as <see cref="FeedbackErrors.Concurrent" />.</summary>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>The one-way hash behind anonymous ratings (BR-027).</summary>
public sealed class RatingHasher(IOptions<FeedbackOptions> options)
{
    public string Hash(Guid ticketId, Guid eventDateId)
    {
        var key = Encoding.UTF8.GetBytes(options.Value.DedupeSecret);
        var data = Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{ticketId:N}|{eventDateId:N}"));

        return Convert.ToHexStringLower(HMACSHA256.HashData(key, data));
    }
}

/// <summary>
///     Ratings (UF-010): a guest rates each date they attended, from the moment they are checked in until
///     <see cref="FeedbackOptions.WindowDays" /> after the date starts; one rating per guest per date, changeable meanwhile. Staff see scores and comments, never who wrote them, and can hide a comment.
/// </summary>
public sealed class FeedbackService(
    IRatingRepository ratings,
    IFeedbackUnitOfWork unitOfWork,
    RatingHasher hasher,
    IEventCatalog events,
    ICustomerDirectory customers,
    IAttendance attendance,
    IOptions<FeedbackOptions> options,
    TimeProvider time)
{
    public async Task<Result<MyFeedbackView>> GetMineAsync(string slug, ApplicationUserId userId, CancellationToken cancellationToken)
    {
        var context = await ContextAsync(slug, userId, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (_, dates) = context.Value;
        var views = new List<MyDateFeedbackView>();

        foreach (var date in dates)
        {
            var existing = await ratings.FindByHashAsync(hasher.Hash(date.TicketId, date.EventDateId), cancellationToken);
            views.Add(new MyDateFeedbackView(
                date.EventDateId,
                date.StartsAt,
                WindowClosesAt(date),
                IsOpen(date),
                existing is null ? null : RatingInputView.From(existing)));
        }

        return new MyFeedbackView(views);
    }

    public async Task<Result<RatingInputView>> SubmitAsync(
        string slug,
        Guid eventDateId,
        ApplicationUserId userId,
        RatingInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var context = await ContextAsync(slug, userId, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (eventId, dates) = context.Value;
        var date = dates.FirstOrDefault(x => x.EventDateId == eventDateId);
        if (date is null)
        {
            return FeedbackErrors.NotAttended;
        }

        if (!IsOpen(date))
        {
            return FeedbackErrors.WindowClosed;
        }

        var hash = hasher.Hash(date.TicketId, date.EventDateId);
        var today = time.GetNairobiToday();
        var existing = await ratings.FindByHashAsync(hash, cancellationToken);

        Rating rating;
        if (existing is null)
        {
            var submitted = Rating.Submit(Guid.CreateVersion7(time.GetUtcNow()), eventId, eventDateId, hash, input, today);
            if (submitted.IsFailure)
            {
                return submitted.Error!;
            }

            rating = submitted.Value!;
            ratings.Add(rating);
        }
        else
        {
            var changed = existing.Change(input, today);
            if (changed.IsFailure)
            {
                return changed.Error!;
            }

            rating = existing;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : RatingInputView.From(rating);
    }

    public async Task<Result<EventFeedbackView>> SummaryAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var list = await ratings.ListForEventAsync(eventId, cancellationToken);

        return new EventFeedbackView(
            eventId,
            Summarise(list),
            [.. list.GroupBy(x => x.EventDateId).Select(x => new DateFeedbackView(x.Key, Summarise([.. x])))]);
    }

    public async Task<Result<IReadOnlyList<FeedbackCommentView>>> CommentsAsync(
        Guid eventId,
        Guid? eventDateId,
        int? limit,
        CancellationToken cancellationToken)
    {
        var list = await ratings.ListCommentsAsync(eventId, eventDateId, Math.Clamp(limit ?? 100, 1, options.Value.MaxPageSize), cancellationToken);

        return Result<IReadOnlyList<FeedbackCommentView>>.Success([.. list.Select(FeedbackCommentView.From)]);
    }

    public async Task<Result<FeedbackCommentView>> SetCommentHiddenAsync(Guid ratingId, bool hidden, CancellationToken cancellationToken)
    {
        var rating = await ratings.FindAsync(ratingId, cancellationToken);
        if (rating is null)
        {
            return FeedbackErrors.RatingNotFound;
        }

        rating.SetCommentHidden(hidden);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : FeedbackCommentView.From(rating);
    }

    public static FeedbackInsights Summarise(IReadOnlyCollection<Rating> list)
    {
        ArgumentNullException.ThrowIfNull(list);

        var distribution = new int[Rating.MaxScore];
        foreach (var rating in list)
        {
            distribution[rating.Overall - 1]++;
        }

        var aspects = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var aspect in Enum.GetValues<RatingAspect>())
        {
            var scores = list.Select(x => x.Score(aspect)).OfType<int>().ToList();
            if (scores.Count > 0)
            {
                aspects[aspect.ToString()] = Math.Round((decimal)scores.Sum() / scores.Count, 2);
            }
        }

        return new FeedbackInsights(
            list.Count,
            list.Count == 0 ? null : Math.Round((decimal)list.Sum(x => x.Overall) / list.Count, 2),
            distribution,
            aspects);
    }

    private DateTimeOffset WindowClosesAt(AttendedDate date) => date.StartsAt.AddDays(options.Value.WindowDays);

    private bool IsOpen(AttendedDate date)
    {
        var now = time.GetUtcNow();
        return now >= date.CheckedInAt && now < WindowClosesAt(date);
    }

    /// <summary>The event and the dates this guest attended, one ticket per date (the lowest id), so a guest holding several tickets rates once.</summary>
    private async Task<Result<(Guid EventId, IReadOnlyList<AttendedDate> Dates)>> ContextAsync(
        string slug,
        ApplicationUserId userId,
        CancellationToken cancellationToken)
    {
        var snapshot = string.IsNullOrWhiteSpace(slug) ? null : await events.FindBySlugAsync(slug, cancellationToken);
        if (snapshot is not { Status: EventLifecycle.Published or EventLifecycle.Completed })
        {
            return FeedbackErrors.EventNotFound;
        }

        var phone = (await customers.FindAsync(userId, cancellationToken))?.Phone;
        if (string.IsNullOrEmpty(phone))
        {
            return FeedbackErrors.PhoneRequired;
        }

        var attended = await attendance.ListAttendedAsync(phone, snapshot.EventId, cancellationToken);
        IReadOnlyList<AttendedDate> dates =
        [
            .. attended
                .GroupBy(x => x.EventDateId)
                .Select(x => x.OrderBy(t => t.TicketId).First())
                .OrderBy(x => x.StartsAt)
        ];

        return (snapshot.EventId, dates);
    }
}

/// <summary><see cref="IFeedbackInsights" /> for Analytics.</summary>
public sealed class FeedbackInsightsProvider(IRatingRepository ratings) : IFeedbackInsights
{
    public async Task<FeedbackInsights> GetAsync(Guid eventId, CancellationToken cancellationToken) =>
        FeedbackService.Summarise(await ratings.ListForEventAsync(eventId, cancellationToken));
}

// ---- Views -------------------------------------------------------------------------------------------------------

public sealed record RatingInputView(int Overall, int? Music, int? Food, int? Venue, int? Organisation, string? Comment, DateOnly SubmittedOn)
{
    public static RatingInputView From(Rating rating)
    {
        ArgumentNullException.ThrowIfNull(rating);
        return new RatingInputView(rating.Overall, rating.Music, rating.Food, rating.Venue, rating.Organisation, rating.Comment, rating.SubmittedOn);
    }
}

public sealed record MyDateFeedbackView(Guid EventDateId, DateTimeOffset StartsAt, DateTimeOffset ClosesAt, bool Open, RatingInputView? Mine);

public sealed record MyFeedbackView(IReadOnlyList<MyDateFeedbackView> Dates);

public sealed record DateFeedbackView(Guid EventDateId, FeedbackInsights Summary);

public sealed record EventFeedbackView(Guid EventId, FeedbackInsights Overall, IReadOnlyList<DateFeedbackView> Dates);

/// <summary>No author: there is nothing on the row to show.</summary>
public sealed record FeedbackCommentView(Guid Id, Guid EventDateId, int Overall, string? Comment, bool Hidden, DateOnly SubmittedOn)
{
    public static FeedbackCommentView From(Rating rating)
    {
        ArgumentNullException.ThrowIfNull(rating);
        return new FeedbackCommentView(rating.Id, rating.EventDateId, rating.Overall, rating.Comment, rating.CommentHidden, rating.SubmittedOn);
    }
}

public sealed record RatingRequest(int? Overall, int? Music, int? Food, int? Venue, int? Organisation, string? Comment)
{
    public RatingInput ToInput() => new(Overall, Music, Food, Venue, Organisation, Comment);
}
