using System.Net;
using Common.Domain.ResultMonad;

namespace Ticketing.Domain.Common;

/// <summary>Error factory for the Ticketing module. Keys are stable API (<c>Ticketing.*</c>); clients branch on them.</summary>
public static class TicketingErrors
{
    /// <summary>
    ///     One 400 for every invalid field at once. Each sub-error is <c>"Field: message"</c>; the client maps the
    ///     prefix before the colon onto its form fields.
    /// </summary>
    public static Error Invalid(string key, IReadOnlyCollection<string> fieldErrors) =>
        new()
        {
            Key = $"Ticketing.{key}",
            Description = "One or more fields are invalid.",
            StatusCode = HttpStatusCode.BadRequest,
            SubErrors = [.. fieldErrors]
        };

    public static Error NotFound(string key, string description) =>
        new()
        {
            Key = $"Ticketing.{key}",
            Description = description,
            StatusCode = HttpStatusCode.NotFound
        };

    /// <summary>The request is well formed but the current state does not allow it (409).</summary>
    public static Error Conflict(string key, string description) =>
        new()
        {
            Key = $"Ticketing.{key}",
            Description = description,
            StatusCode = HttpStatusCode.Conflict
        };

    public const string ConcurrentUpdateKey = "Ticketing.ConcurrentUpdate";

    /// <summary>Someone else changed the same record first (optimistic concurrency); re-read and decide again.</summary>
    public static Error ConcurrentUpdate =>
        Conflict("ConcurrentUpdate", "This was changed by someone else at the same moment. Try again.");

    public static Error EventNotFound =>
        NotFound("EventNotFound", "The event was not found.");

    public static Error WaitlistClosed =>
        Conflict("WaitlistClosed", "The waitlist for this event is closed.");

    public static Error WaitlistAlreadyJoined =>
        Conflict("WaitlistAlreadyJoined", "This phone number is already on the waitlist for this event.");
}
