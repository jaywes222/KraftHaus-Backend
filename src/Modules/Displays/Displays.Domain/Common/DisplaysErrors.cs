using System.Net;
using Common.Domain.ResultMonad;

namespace Displays.Domain.Common;

/// <summary>Error factory for the Displays module. Keys are stable API (<c>Displays.*</c>); clients branch on them.</summary>
public static class DisplaysErrors
{
    /// <summary>One 400 for every invalid field at once; each sub-error is <c>"Field: message"</c>.</summary>
    public static Error Invalid(string key, IReadOnlyCollection<string> fieldErrors) =>
        new()
        {
            Key = $"Displays.{key}",
            Description = "One or more fields are invalid.",
            StatusCode = HttpStatusCode.BadRequest,
            SubErrors = [.. fieldErrors]
        };

    public static Error NotFound(string key, string description) =>
        new()
        {
            Key = $"Displays.{key}",
            Description = description,
            StatusCode = HttpStatusCode.NotFound
        };

    public static Error Conflict(string key, string description) =>
        new()
        {
            Key = $"Displays.{key}",
            Description = description,
            StatusCode = HttpStatusCode.Conflict
        };

    public static Error Forbidden(string key, string description) =>
        new()
        {
            Key = $"Displays.{key}",
            Description = description,
            StatusCode = HttpStatusCode.Forbidden
        };

    public const string ConcurrentUpdateKey = "Displays.ConcurrentUpdate";

    public static Error ConcurrentUpdate =>
        Conflict("ConcurrentUpdate", "This was changed by someone else at the same moment. Try again.");

    public static Error EventNotFound => NotFound("EventNotFound", "The event was not found.");

    public static Error DeviceNotFound => NotFound("DeviceNotFound", "The screen was not found.");

    public static Error ConfigurationNotFound => NotFound("ConfigurationNotFound", "The display configuration was not found.");

    public static Error SlotNotFound => NotFound("SlotNotFound", "The programme slot was not found.");

    public static Error AssetNotFound => NotFound("AssetNotFound", "The image was not found.");
}
