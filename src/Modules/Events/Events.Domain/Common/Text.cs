using Common.Domain.ResultMonad;

namespace Events.Domain.Common;

/// <summary>Small helpers for validating free text on the way into the aggregates.</summary>
internal static class Text
{
    public static Result<string> Required(string? value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return EventsErrors.Required(parameterName, $"{parameterName} is required.");
        }

        var trimmed = value.Trim();

        return trimmed.Length > maxLength
            ? EventsErrors.Invalid(parameterName, $"{parameterName} must be at most {maxLength} characters.")
            : trimmed;
    }

    public static Result<string?> Optional(string? value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<string?>.Success(null);
        }

        var trimmed = value.Trim();

        return trimmed.Length > maxLength
            ? EventsErrors.Invalid(parameterName, $"{parameterName} must be at most {maxLength} characters.")
            : Result<string?>.Success(trimmed);
    }

    public static Result<Uri?> OptionalAbsoluteUrl(Uri? value, string parameterName)
    {
        if (value is null)
        {
            return Result<Uri?>.Success(null);
        }

        return value.IsAbsoluteUri && (value.Scheme == Uri.UriSchemeHttps || value.Scheme == Uri.UriSchemeHttp)
            ? Result<Uri?>.Success(value)
            : EventsErrors.Invalid(parameterName, $"{parameterName} must be an absolute http(s) URL.");
    }
}
