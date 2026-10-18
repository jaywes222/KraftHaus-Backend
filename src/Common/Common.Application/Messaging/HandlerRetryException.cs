using Common.Domain.ResultMonad;

namespace Common.Application.Messaging;

/// <summary>
///     Thrown by an integration-event handler whose change could not be saved (typically a concurrent update): the
///     outbox records the failure and delivers the event again with backoff.
/// </summary>
public sealed class HandlerRetryException : Exception
{
    public HandlerRetryException()
    {
    }

    public HandlerRetryException(string message)
        : base(message)
    {
    }

    public HandlerRetryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public HandlerRetryException(Error error)
        : base($"{error?.Key}: {error?.Description}")
    {
    }

    public static void ThrowIfFailed(IResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Error is { } error)
        {
            throw new HandlerRetryException(error);
        }
    }
}
