using System.Net;
using Common.Domain.ResultMonad;

namespace Payments.Domain.Common;

/// <summary>Error factory for the Payments module. Keys are stable API (<c>Payments.*</c>).</summary>
public static class PaymentsErrors
{
    public static Error Invalid(string parameterName, string description) =>
        new()
        {
            Key = $"Payments.{parameterName}Invalid",
            Description = description,
            ParameterName = parameterName
        };

    public static Error NotFound(Guid id) =>
        new()
        {
            Key = "Payments.PaymentNotFound",
            Description = $"Payment '{id}' was not found.",
            StatusCode = HttpStatusCode.NotFound
        };

    public static Error TillNotFound(Guid id) =>
        new()
        {
            Key = "Payments.TillNotFound",
            Description = $"Till '{id}' was not found.",
            StatusCode = HttpStatusCode.NotFound
        };

    /// <summary>One 400 for every invalid field at once; each sub-error is <c>"Field: message"</c>.</summary>
    public static Error Fields(string key, IReadOnlyCollection<string> fieldErrors) =>
        new()
        {
            Key = $"Payments.{key}",
            Description = "One or more fields are invalid.",
            StatusCode = HttpStatusCode.BadRequest,
            SubErrors = [.. fieldErrors]
        };

    public static Error InvalidState(string key, string description) =>
        new()
        {
            Key = $"Payments.{key}",
            Description = description,
            StatusCode = HttpStatusCode.Conflict
        };
}
