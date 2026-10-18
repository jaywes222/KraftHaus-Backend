using System.Net;
using Common.Domain.ResultMonad;

namespace Commerce.Domain.Common;

/// <summary>Error factory for the Commerce module. Keys are stable API (<c>Commerce.*</c>); clients branch on them.</summary>
public static class CommerceErrors
{
    /// <summary>One 400 for every invalid field at once; each sub-error is <c>"Field: message"</c>.</summary>
    public static Error Invalid(string key, IReadOnlyCollection<string> fieldErrors) =>
        new()
        {
            Key = $"Commerce.{key}",
            Description = "One or more fields are invalid.",
            StatusCode = HttpStatusCode.BadRequest,
            SubErrors = [.. fieldErrors]
        };

    public static Error NotFound(string key, string description) =>
        new()
        {
            Key = $"Commerce.{key}",
            Description = description,
            StatusCode = HttpStatusCode.NotFound
        };

    public static Error Conflict(string key, string description) =>
        new()
        {
            Key = $"Commerce.{key}",
            Description = description,
            StatusCode = HttpStatusCode.Conflict
        };

    public static Error Forbidden(string key, string description) =>
        new()
        {
            Key = $"Commerce.{key}",
            Description = description,
            StatusCode = HttpStatusCode.Forbidden
        };

    public const string ConcurrentUpdateKey = "Commerce.ConcurrentUpdate";

    public static Error ConcurrentUpdate =>
        Conflict("ConcurrentUpdate", "This was changed by someone else at the same moment. Try again.");

    public static Error KraftNotFound => NotFound("KraftNotFound", "The Kraft was not found.");

    public static Error EventNotFound => NotFound("EventNotFound", "The event was not found.");
}
