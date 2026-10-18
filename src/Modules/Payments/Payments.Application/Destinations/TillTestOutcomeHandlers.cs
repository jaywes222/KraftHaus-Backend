using Common.Application.Messaging;
using Common.IntegrationEvents.Payments;
using Payments.Application.Abstractions;

namespace Payments.Application.Destinations;

/// <summary>Records the outcome of a till test payment on the till (UF-017).</summary>
public sealed class TillTestSucceededHandler(
    IPaymentDestinationRepository destinations,
    IPaymentsUnitOfWork unitOfWork,
    TimeProvider time)
    : IIntegrationEventHandler<PaymentSucceeded>
{
    public const string HandlerName = "payments.till-test-succeeded";

    public async Task HandleAsync(PaymentSucceeded @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (@event.Purpose == PaymentPurpose.TillTest)
        {
            await TillTestOutcome.RecordAsync(destinations, unitOfWork, time, @event.Id, HandlerName, @event.PaymentId, passed: true, cancellationToken);
        }
    }
}

public sealed class TillTestFailedHandler(
    IPaymentDestinationRepository destinations,
    IPaymentsUnitOfWork unitOfWork,
    TimeProvider time)
    : IIntegrationEventHandler<PaymentFailed>
{
    public const string HandlerName = "payments.till-test-failed";

    public async Task HandleAsync(PaymentFailed @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (@event.Purpose == PaymentPurpose.TillTest)
        {
            await TillTestOutcome.RecordAsync(destinations, unitOfWork, time, @event.Id, HandlerName, @event.PaymentId, passed: false, cancellationToken);
        }
    }
}

internal static class TillTestOutcome
{
    public static async Task RecordAsync(
        IPaymentDestinationRepository destinations,
        IPaymentsUnitOfWork unitOfWork,
        TimeProvider time,
        Guid messageId,
        string handler,
        Guid paymentId,
        bool passed,
        CancellationToken cancellationToken)
    {
        if (await unitOfWork.AlreadyProcessedAsync(messageId, handler, cancellationToken))
        {
            return;
        }

        var till = await destinations.FindByTestPaymentAsync(paymentId, cancellationToken);

        unitOfWork.MarkProcessed(messageId, handler);
        till?.RecordTestResult(paymentId, passed, time.GetUtcNow());

        var saved = await unitOfWork.TrySaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            // Someone changed the till at the same moment: let the outbox deliver the message again.
            throw new HandlerRetryException(saved.Error!);
        }
    }
}
