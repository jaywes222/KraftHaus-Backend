using System.Net;

namespace Common.Domain.ResultMonad;

public class Error
{
    public required string Key { get; init; }
    public string? Description { get; init; }
    public string? ParameterName { get; init; }
    public object? Value { get; init; }
    public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.BadRequest;
    public ICollection<string>? SubErrors { get; init; }

    public static Error Validation(ICollection<string> errors)
    {
        return new Error
        {
            Key = nameof(Validation),
            Description = "One or more validation errors occurred.",
            StatusCode = HttpStatusCode.BadRequest,
            SubErrors = errors
        };
    }

    public static Error NotFound(string parameterName, object? value = null)
    {
        return new Error
        {
            Key = nameof(NotFound),
            Description = value is not null
                ? $"{parameterName} with value '{value}' was not found."
                : $"{parameterName} was not found.",
            ParameterName = parameterName,
            Value = value,
            StatusCode = HttpStatusCode.NotFound
        };
    }

    public static Error ViolatesUniqueConstraint(string parameterName, object? value = null)
    {
        return new Error
        {
            Key = nameof(ViolatesUniqueConstraint),
            Description = value is not null
                ? $"{parameterName} with value '{value}' already exists."
                : $"{parameterName} violates a unique constraint.",
            ParameterName = parameterName,
            Value = value,
            StatusCode = HttpStatusCode.Conflict
        };
    }

    /// <summary>
    /// Represents the situation where user is authorized to access/operate only his/her own resources but tries someone else's resources.
    /// </summary>
    public static Error NotOwned<T>(object? value = null)
    {
        var typeName = typeof(T).Name;
        return new Error
        {
            Key = nameof(NotOwned),
            Description = value is not null
                ? $"Access denied for resource '{typeName}' with value '{value}'."
                : $"Access denied for resource '{typeName}'.",
            ParameterName = typeName,
            Value = value,
            StatusCode = HttpStatusCode.Forbidden
        };
    }

    /// <summary>
    /// Represents the situation where value to change is the same as before.
    /// </summary>
    public static Error SameValue(string parameterName, object value)
    {
        return new Error
        {
            Key = nameof(SameValue),
            Description = $"The property '{parameterName}' already has the value '{value}'.",
            ParameterName = parameterName,
            Value = value,
            StatusCode = HttpStatusCode.BadRequest
        };
    }
}
