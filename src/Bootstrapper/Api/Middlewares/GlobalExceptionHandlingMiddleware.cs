using System.Net;
using Common.Application.Extensions;
using EntityFramework.Exceptions.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Middlewares;

internal sealed partial class GlobalExceptionHandlingMiddleware(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandlingMiddleware> logger
) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await HandleExceptionAsync(
                context,
                ex,
                (int)HttpStatusCode.Conflict,
                "The resource was modified by another request. Please refresh and try again.");
        }
        catch (UniqueConstraintException ex)
        {
            await HandleExceptionAsync(
                context,
                ex,
                (int)HttpStatusCode.Conflict,
                "A record with the same value already exists.");
        }
        catch (CannotInsertNullException ex)
        {
            await HandleExceptionAsync(
                context,
                ex,
                (int)HttpStatusCode.BadRequest,
                "A required value is missing.");
        }
        catch (MaxLengthExceededException ex)
        {
            await HandleExceptionAsync(
                context,
                ex,
                (int)HttpStatusCode.BadRequest,
                "One or more values exceed the allowed length.");
        }
        catch (NumericOverflowException ex)
        {
            await HandleExceptionAsync(
                context,
                ex,
                (int)HttpStatusCode.BadRequest,
                "A numeric value is outside the allowed range.");
        }
        catch (ReferenceConstraintException ex)
        {
            await HandleExceptionAsync(
                context,
                ex,
                (int)HttpStatusCode.BadRequest,
                "The requested operation violates a data relationship.");
        }
        catch (DbUpdateException ex)
        {
            await HandleExceptionAsync(
                context,
                ex,
                (int)HttpStatusCode.InternalServerError,
                "An unexpected database error occurred.");
        }
        catch (BadHttpRequestException ex)
        {
            await HandleExceptionAsync(
                context,
                ex,
                (int)HttpStatusCode.BadRequest,
                "The request was invalid.");
        }
        catch (OperationCanceledException ex)
        {
            if (context.RequestAborted.IsCancellationRequested)
            {
                LogRequestAborted(logger);
                return;
            }

            await HandleExceptionAsync(
                context,
                ex,
                StatusCodes.Status499ClientClosedRequest,
                "The request was cancelled.");
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            await HandleExceptionAsync(
                context,
                ex,
                (int)HttpStatusCode.InternalServerError,
                "An unexpected error occurred.");
        }
#pragma warning restore CA1031
    }

    private async Task HandleExceptionAsync(
        HttpContext context,
        Exception exception,
        int statusCode,
        string title)
    {
        LogError(logger, exception);

        if (context.Response.HasStarted)
        {
            LogCantWriteResponse(logger);
            return;
        }

        var formattedTitle = $"{title} Trace ID: {context.TraceIdentifier}";

        var details = new ProblemDetails
        {
            Status = statusCode,
            Title = formattedTitle
        };

        context.Response.StatusCode = statusCode;

        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = details
        });
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unhandled exception occurred.")]
    private static partial void LogError(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Response has already started, can't write response.")]
    private static partial void LogCantWriteResponse(
        ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Request aborted by client before completion.")]
    private static partial void LogRequestAborted(
        ILogger logger);
}
