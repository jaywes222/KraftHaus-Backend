using System.Net;
using Common.Domain.ResultMonad;

namespace Communications.Domain.Common;

/// <summary>Error factory for the Communications module. Keys are stable API (<c>Communications.*</c>); clients branch on them.</summary>
public static class CommunicationsErrors
{
    public static Error Invalid(string key, IReadOnlyCollection<string> fieldErrors) =>
        new()
        {
            Key = $"Communications.{key}",
            Description = "One or more fields are invalid.",
            StatusCode = HttpStatusCode.BadRequest,
            SubErrors = [.. fieldErrors]
        };

    public static Error NotFound(string key, string description) =>
        new()
        {
            Key = $"Communications.{key}",
            Description = description,
            StatusCode = HttpStatusCode.NotFound
        };

    public static Error Conflict(string key, string description) =>
        new()
        {
            Key = $"Communications.{key}",
            Description = description,
            StatusCode = HttpStatusCode.Conflict
        };

    public static Error Forbidden(string key, string description) =>
        new()
        {
            Key = $"Communications.{key}",
            Description = description,
            StatusCode = HttpStatusCode.Forbidden
        };

    public const string ConcurrentUpdateKey = "Communications.ConcurrentUpdate";

    public static Error ConcurrentUpdate =>
        Conflict("ConcurrentUpdate", "This was changed by someone else at the same moment. Try again.");

    public static Error BroadcastNotFound => NotFound("BroadcastNotFound", "The broadcast was not found.");

    public static Error TemplateNotFound => NotFound("TemplateNotFound", "The template was not found.");

    public static Error EventNotFound => NotFound("EventNotFound", "The event was not found.");

    public static Error OptOutNotFound => NotFound("OptOutNotFound", "That number is not on the opt-out list.");

    public static Error MessageNotFound => NotFound("MessageNotFound", "The message was not found.");

    public static Error NotEditable =>
        Conflict("NotEditable", "Only a draft can be changed. Cancel it and make a new one.");

    public static Error AlreadyFinished =>
        Conflict("AlreadyFinished", "This broadcast has already been sent or cancelled.");

    public static Error NotCancellable =>
        Conflict("NotCancellable", "A broadcast that has started sending can't be cancelled.");

    public static Error PhoneRequired =>
        Forbidden("PhoneRequired", "Message preferences belong to a mobile number. Sign in with your phone.");
}
