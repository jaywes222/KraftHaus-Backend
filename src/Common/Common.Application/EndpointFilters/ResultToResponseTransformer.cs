using Common.Application.Extensions;
using Common.Domain.ResultMonad;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using AspNetResult = Microsoft.AspNetCore.Http.IResult;

namespace Common.Application.EndpointFilters;

internal sealed class ResultToResponseTransformer(IWebHostEnvironment env)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var resultObj = await next(context);

        // An inner filter or the framework itself (e.g. a disabled RequireFeature check,
        // minimal-API parameter binding) can short-circuit before a Result is produced,
        // yielding an ASP.NET IResult. Our Result implements only the domain IResult, so
        // it never matches here — pass the short-circuit response through untouched.
        if (resultObj is AspNetResult shortCircuit)
        {
            return shortCircuit;
        }

        if (resultObj is not Result result)
        {
            throw new InvalidOperationException(
                $"{nameof(ResultToResponseTransformer)} can only be used with Result type.");
        }

        return result.Match(
            () => Results.NoContent(),
            error =>
            {
                var problemDetails = new ProblemDetails
                {
                    Status = (int)error.StatusCode,
                    Title = error.Description ?? error.Key,
                    Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path.Value}"
                };

                problemDetails.AddErrorKey(error.Key);
                problemDetails.AddErrors(error.SubErrors ?? Array.Empty<string>());

                if (error.Value is not null)
                {
                    problemDetails.Extensions.TryAdd("detail", error.Value);
                }

                problemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
                problemDetails.Extensions.TryAdd("environment", env.EnvironmentName);

                return Results.Problem(problemDetails);
            }
        );
    }
}

internal sealed class ResultToCreatedResponseTransformer<T>(IWebHostEnvironment env)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var resultObj = await next(context);

        // See ResultToResponseTransformer above for why this check exists.
        if (resultObj is AspNetResult shortCircuit)
        {
            return shortCircuit;
        }

        if (resultObj is not Result<T> result)
        {
            throw new InvalidOperationException(
                $"{nameof(ResultToCreatedResponseTransformer<T>)} can only be used with Result<{nameof(T)}> type.");
        }

        return result.Match(
            value => Results.Created((Uri?)null, value),
            error =>
            {
                var problemDetails = new ProblemDetails
                {
                    Status = (int)error.StatusCode,
                    Title = error.Description ?? error.Key,
                    Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path.Value}"
                };

                problemDetails.AddErrorKey(error.Key);
                problemDetails.AddErrors(error.SubErrors ?? Array.Empty<string>());

                if (error.Value is not null)
                {
                    problemDetails.Extensions.TryAdd("detail", error.Value);
                }

                problemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
                problemDetails.Extensions.TryAdd("environment", env.EnvironmentName);

                return Results.Problem(problemDetails);
            }
        );
    }
}

internal sealed class ResultToResponseTransformer<T>(IWebHostEnvironment env)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var resultObj = await next(context);

        // See ResultToResponseTransformer above for why this check exists.
        if (resultObj is AspNetResult shortCircuit)
        {
            return shortCircuit;
        }

        if (resultObj is not Result<T> result)
        {
            throw new InvalidOperationException(
                $"{nameof(ResultToResponseTransformer<T>)} can only be used with Result<{nameof(T)}> type.");
        }

        return result.Match(
            value => Results.Ok(value),
            error =>
            {
                var problemDetails = new ProblemDetails
                {
                    Status = (int)error.StatusCode,
                    Title = error.Description ?? error.Key,
                    Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path.Value}"
                };

                problemDetails.AddErrorKey(error.Key);
                problemDetails.AddErrors(error.SubErrors ?? Array.Empty<string>());

                if (error.Value is not null)
                {
                    problemDetails.Extensions.TryAdd("detail", error.Value);
                }

                problemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
                problemDetails.Extensions.TryAdd("environment", env.EnvironmentName);

                return Results.Problem(problemDetails);
            }
        );
    }
}
