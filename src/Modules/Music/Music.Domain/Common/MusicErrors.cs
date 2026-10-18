using System.Net;
using Common.Domain.ResultMonad;

namespace Music.Domain.Common;

/// <summary>Error factory for the Music module. Keys are stable API (<c>Music.*</c>); clients branch on them.</summary>
public static class MusicErrors
{
    /// <summary>One 400 for every invalid field at once; each sub-error is <c>"Field: message"</c>.</summary>
    public static Error Invalid(string key, IReadOnlyCollection<string> fieldErrors) =>
        new()
        {
            Key = $"Music.{key}",
            Description = "One or more fields are invalid.",
            StatusCode = HttpStatusCode.BadRequest,
            SubErrors = [.. fieldErrors]
        };

    public static Error NotFound(string key, string description) =>
        new()
        {
            Key = $"Music.{key}",
            Description = description,
            StatusCode = HttpStatusCode.NotFound
        };

    public static Error Conflict(string key, string description) =>
        new()
        {
            Key = $"Music.{key}",
            Description = description,
            StatusCode = HttpStatusCode.Conflict
        };

    public static Error Forbidden(string key, string description) =>
        new()
        {
            Key = $"Music.{key}",
            Description = description,
            StatusCode = HttpStatusCode.Forbidden
        };

    public static Error Unavailable(string key, string description) =>
        new()
        {
            Key = $"Music.{key}",
            Description = description,
            StatusCode = HttpStatusCode.ServiceUnavailable
        };

    public const string ConcurrentUpdateKey = "Music.ConcurrentUpdate";

    public static Error ConcurrentUpdate =>
        Conflict("ConcurrentUpdate", "This was changed by someone else at the same moment. Try again.");

    public static Error EventNotFound => NotFound("EventNotFound", "The event was not found.");

    public static Error RequestNotFound => NotFound("RequestNotFound", "The song request was not found.");

    public static Error RequestsClosed =>
        Conflict("RequestsClosed", "Song requests aren't open right now.");

    public static Error TicketRequired =>
        Forbidden("TicketRequired", "Song requests are for ticket holders. Sign in with the number your ticket is on.");

    public static Error PhoneRequired =>
        Forbidden("PhoneRequired", "Song requests are tied to your ticket's mobile number. Sign in with your phone.");

    public static Error AlreadyRequested =>
        Conflict("AlreadyRequested", "You've already asked for this song.");

    public static Error LimitReached(int limit) =>
        Conflict("LimitReached", $"You've used all {limit} of your requests for this event.");

    public static Error CatalogUnavailable =>
        Unavailable("CatalogUnavailable", "Song search is busy. Try again in a moment, or type the song in.");

    public static Error TrackNotFound =>
        NotFound("TrackNotFound", "That song couldn't be found. Search again, or type it in.");
}
