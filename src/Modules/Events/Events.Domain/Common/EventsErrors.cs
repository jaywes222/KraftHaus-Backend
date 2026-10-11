using System.Net;
using Common.Domain.ResultMonad;

namespace Events.Domain.Common;

/// <summary>Error factory for the Events module. Keys are stable API (<c>Events.*</c>); clients branch on them.</summary>
public static class EventsErrors
{
    public static Error Required(string parameterName, string description) =>
        new()
        {
            Key = $"Events.{parameterName}Required",
            Description = description,
            ParameterName = parameterName
        };

    public static Error Invalid(string parameterName, string description) =>
        new()
        {
            Key = $"Events.{parameterName}Invalid",
            Description = description,
            ParameterName = parameterName
        };

    /// <summary>The request is well formed but the aggregate's current state does not allow it (409).</summary>
    public static Error InvalidState(string key, string description) =>
        new()
        {
            Key = $"Events.{key}",
            Description = description,
            StatusCode = HttpStatusCode.Conflict
        };

    public static Error NotFound(string parameterName, Guid value) =>
        new()
        {
            Key = $"Events.{parameterName}NotFound",
            Description = $"{parameterName} '{value}' was not found.",
            ParameterName = parameterName,
            StatusCode = HttpStatusCode.NotFound
        };

    public static Error Conflict(string key, string description) =>
        new()
        {
            Key = $"Events.{key}",
            Description = description,
            StatusCode = HttpStatusCode.Conflict
        };
}
