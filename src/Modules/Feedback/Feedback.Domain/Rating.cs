using System.Net;
using Common.Domain.ResultMonad;

namespace Feedback.Domain;

/// <summary>Permissions the Feedback endpoints require; must match Identity's catalog (an API test keeps them in step).</summary>
public static class FeedbackPermission
{
    /// <summary>Read ratings and comments (Tenant Admin, Support).</summary>
    public const string View = "feedback:view";

    /// <summary>Hide or show a comment: the same people who run the event (Tenant Admin).</summary>
    public const string Moderate = "events:manage";

    public static IReadOnlyList<string> All => [View, Moderate];
}

/// <summary>The parts of the night a guest may score besides the overall rating.</summary>
public enum RatingAspect
{
    Music = 1,
    Food = 2,
    Venue = 3,
    Organisation = 4
}

public static class FeedbackErrors
{
    public static Error Invalid(IReadOnlyCollection<string> fieldErrors) =>
        new()
        {
            Key = "Feedback.RatingInvalid",
            Description = "One or more fields are invalid.",
            StatusCode = HttpStatusCode.BadRequest,
            SubErrors = [.. fieldErrors]
        };

    public static Error EventNotFound => new()
    {
        Key = "Feedback.EventNotFound",
        Description = "The event was not found.",
        StatusCode = HttpStatusCode.NotFound
    };

    public static Error RatingNotFound => new()
    {
        Key = "Feedback.RatingNotFound",
        Description = "The rating was not found.",
        StatusCode = HttpStatusCode.NotFound
    };

    public static Error NotAttended => new()
    {
        Key = "Feedback.NotAttended",
        Description = "Ratings are for guests who were checked in on that date.",
        StatusCode = HttpStatusCode.Forbidden
    };

    public static Error PhoneRequired => new()
    {
        Key = "Feedback.PhoneRequired",
        Description = "Ratings are tied to your ticket's mobile number. Sign in with your phone.",
        StatusCode = HttpStatusCode.Forbidden
    };

    public static Error WindowClosed => new()
    {
        Key = "Feedback.WindowClosed",
        Description = "Ratings for this date are closed.",
        StatusCode = HttpStatusCode.Conflict
    };

    public static Error Concurrent => new()
    {
        Key = "Feedback.ConcurrentUpdate",
        Description = "Your rating was saved from another tab at the same moment. Refresh and try again.",
        StatusCode = HttpStatusCode.Conflict
    };
}

/// <summary>
///     One guest's anonymous rating of one event date (UF-010, BR-027). There is deliberately no customer, phone, ticket
///     or user id on the row, no created-by stamp and no audit trail of who submitted: only a one-way
///     <see cref="DedupeHash" /> (HMAC of the ticket and date with a server secret) that lets the same guest change their
///     rating and stops a second one. The day is kept, not the time, so a rating cannot be matched to a check-in.
/// </summary>
public sealed class Rating
{
    public const int MinScore = 1;
    public const int MaxScore = 5;
    public const int CommentMaxLength = 500;
    public const int HashLength = 64;

    private Rating()
    {
    }

    public Guid Id { get; private set; }

    public Guid EventId { get; private set; }

    public Guid EventDateId { get; private set; }

    /// <summary>Lower-case hex HMAC-SHA256; unique.</summary>
    public string DedupeHash { get; private set; } = string.Empty;

    public int Overall { get; private set; }

    public int? Music { get; private set; }

    public int? Food { get; private set; }

    public int? Venue { get; private set; }

    public int? Organisation { get; private set; }

    public string? Comment { get; private set; }

    /// <summary>Nairobi calendar day of the latest change.</summary>
    public DateOnly SubmittedOn { get; private set; }

    /// <summary>Staff hid the comment (abuse, personal data). The scores still count.</summary>
    public bool CommentHidden { get; private set; }

    public static Result<Rating> Submit(
        Guid id,
        Guid eventId,
        Guid eventDateId,
        string dedupeHash,
        RatingInput input,
        DateOnly today)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupeHash);
        ArgumentNullException.ThrowIfNull(input);

        var rating = new Rating { Id = id, EventId = eventId, EventDateId = eventDateId, DedupeHash = dedupeHash };
        var applied = rating.Change(input, today);

        return applied.IsFailure ? applied.Error! : rating;
    }

    public Result Change(RatingInput input, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);

        var errors = new List<string>();
        Check(errors, "Overall", input.Overall, required: true);
        Check(errors, "Music", input.Music, required: false);
        Check(errors, "Food", input.Food, required: false);
        Check(errors, "Venue", input.Venue, required: false);
        Check(errors, "Organisation", input.Organisation, required: false);

        var comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        if (comment is { Length: > CommentMaxLength })
        {
            errors.Add($"Comment: Keep it under {CommentMaxLength} characters.");
        }

        if (errors.Count > 0)
        {
            return FeedbackErrors.Invalid(errors);
        }

        if (comment != Comment)
        {
            CommentHidden = false;
        }

        Overall = input.Overall!.Value;
        Music = input.Music;
        Food = input.Food;
        Venue = input.Venue;
        Organisation = input.Organisation;
        Comment = comment;
        SubmittedOn = today;

        return Result.Success;
    }

    public void SetCommentHidden(bool hidden) => CommentHidden = hidden && Comment is not null;

    public int? Score(RatingAspect aspect) =>
        aspect switch
        {
            RatingAspect.Music => Music,
            RatingAspect.Food => Food,
            RatingAspect.Venue => Venue,
            RatingAspect.Organisation => Organisation,
            _ => null
        };

    private static void Check(List<string> errors, string field, int? score, bool required)
    {
        if (score is null)
        {
            if (required)
            {
                errors.Add($"{field}: Choose {MinScore}-{MaxScore} stars.");
            }

            return;
        }

        if (score is < MinScore or > MaxScore)
        {
            errors.Add($"{field}: Choose {MinScore}-{MaxScore} stars.");
        }
    }
}

/// <param name="Overall">Required, 1-5.</param>
public sealed record RatingInput(int? Overall, int? Music, int? Food, int? Venue, int? Organisation, string? Comment);
