using Common.Application.Messaging;
using Common.Domain.ResultMonad;
using Microsoft.Extensions.Logging;
using Payments.Application.Abstractions;
using Payments.Application.Provider;
using Payments.Domain.Payments;

namespace Payments.Application.Payments;

/// <summary>Asks PalPluss for its record of a payment and applies it (callbacks, the status job and Finance "check now").</summary>
public sealed partial class PaymentStatusSync(
    IPaymentProvider provider,
    IPaymentsUnitOfWork unitOfWork,
    IOutboxSignal outboxSignal,
    TimeProvider time,
    ILogger<PaymentStatusSync> logger)
{
    /// <summary>
    ///     <c>false</c> when the provider could not be reached (the caller may retry later). A missing transaction id
    ///     (prompt never confirmed) is not checked.
    /// </summary>
    public async Task<bool> SyncAsync(Payment payment, CancellationToken cancellationToken, string? receiptHint = null)
    {
        ArgumentNullException.ThrowIfNull(payment);

        if (payment.ProviderTransactionId is null)
        {
            return true;
        }

        var lookup = await provider.GetTransactionAsync(payment.ProviderTransactionId, cancellationToken);

        switch (lookup)
        {
            case ProviderLookup.Found found:
                // PalPluss's transaction record may not carry the receipt; the callback does. Only cosmetic: the
                // status and amount still come from the record.
                var record = found.Record.MpesaReceipt is null && receiptHint is { Length: > 0 and <= 32 }
                    ? found.Record with { MpesaReceipt = receiptHint }
                    : found.Record;
                var transition = payment.ApplyProviderRecord(record, time.GetUtcNow());
                var published = PaymentEventPublisher.Publish(unitOfWork, payment, transition);
                await unitOfWork.SaveChangesAsync(cancellationToken);

                if (published)
                {
                    outboxSignal.Notify();
                }

                return true;

            case ProviderLookup.NotFound:
                LogTransactionMissing(logger, payment.Id.Value);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Stops waiting for a payment that has had no final answer for too long.</summary>
    public async Task ExpireAsync(Payment payment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);

        var transition = payment.Expire(time.GetUtcNow());
        if (PaymentEventPublisher.Publish(unitOfWork, payment, transition))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            outboxSignal.Notify();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "PalPluss has no record of the transaction for payment {PaymentId}.")]
    private static partial void LogTransactionMissing(ILogger logger, Guid paymentId);
}

/// <summary>
///     <c>POST /webhooks/palpluss/{token}</c>. PalPluss callbacks are unsigned, so the body is only used to learn the
///     transaction id; the outcome always comes from PalPluss's own record. The per-payment token in the URL is the
///     only way to reach a payment.
/// </summary>
public sealed class PaymentCallbackService(
    IPaymentRepository payments,
    PaymentStatusSync sync,
    IPaymentsUnitOfWork unitOfWork,
    TimeProvider time)
{
    public async Task<Result> HandleAsync(string token, CallbackHints hints, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hints);
        var transactionIdFromBody = hints.TransactionId;

        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return Domain.Common.PaymentsErrors.NotFound(Guid.Empty);
        }

        var payment = await payments.FindByCallbackTokenHashAsync(Payment.HashToken(token), cancellationToken);
        if (payment is null)
        {
            return Domain.Common.PaymentsErrors.NotFound(Guid.Empty);
        }

        if (payment.ProviderTransactionId is null && !string.IsNullOrWhiteSpace(transactionIdFromBody))
        {
            // The start request timed out after PalPluss had already sent the prompt.
            payment.AdoptProviderTransaction(transactionIdFromBody, time.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // A receipt is only taken from a callback that names this payment's own transaction.
        var receipt = string.Equals(hints.TransactionId, payment.ProviderTransactionId, StringComparison.OrdinalIgnoreCase)
            ? hints.MpesaReceipt
            : null;

        return await sync.SyncAsync(payment, cancellationToken, receipt)
            ? Result.Success
            : new Error
            {
                Key = "Payments.ProviderUnavailable",
                Description = "PalPluss could not be reached to confirm the transaction; send the callback again.",
                StatusCode = System.Net.HttpStatusCode.ServiceUnavailable
            };
    }
}

/// <summary>What an unsigned callback body claims: used to find the transaction and to fill in the receipt, never for the outcome.</summary>
public sealed record CallbackHints(string? TransactionId, string? MpesaReceipt)
{
    public static CallbackHints None { get; } = new(null, null);
}
