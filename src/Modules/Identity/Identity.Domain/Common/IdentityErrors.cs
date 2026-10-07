using Common.Domain.ResultMonad;

namespace Identity.Domain.Common;

public static class IdentityErrors
{
    public static Error Required(
        string parameterName,
        string description)
    {
        return new Error
        {
            Key = $"Identity.{parameterName}Required",
            Description = description,
            ParameterName = parameterName
        };
    }

    public static Error Invalid(
        string parameterName,
        string description)
    {
        return new Error
        {
            Key = $"Identity.{parameterName}Invalid",
            Description = description,
            ParameterName = parameterName
        };
    }

    public static Error InvalidState(
        string key,
        string description)
    {
        return new Error
        {
            Key = $"Identity.{key}",
            Description = description
        };
    }

    public static Error Forbidden(
        string key,
        string description)
    {
        return new Error
        {
            Key = $"Identity.{key}",
            Description = description,
            StatusCode = System.Net.HttpStatusCode.Forbidden
        };
    }

    public static Error Unauthorized(
        string key,
        string description)
    {
        return new Error
        {
            Key = $"Identity.{key}",
            Description = description,
            StatusCode = System.Net.HttpStatusCode.Unauthorized
        };
    }
}
