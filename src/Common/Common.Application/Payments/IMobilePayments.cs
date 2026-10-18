using Common.IntegrationEvents.Payments;

namespace Common.Application.Payments;

/// <summary>
///     Starts and reads M-Pesa (STK push) payments. Implemented by the Payments module; the outcome is delivered
///     later as <see cref="PaymentSucceeded" /> / <see cref="PaymentFailed" /> through the tenant outbox.
/// </summary>
public interface IMobilePayments
{
    /// <summary>
    ///     Creates the payment and asks the provider to send the prompt. Idempotent on
    ///     <see cref="StartMobilePayment.PaymentId" />: a repeat returns the existing payment without a second prompt.
    ///     A provider refusal is not an error here: the view comes back <see cref="MobilePaymentStatus.Failed" /> with
    ///     an <see cref="MobilePaymentView.ErrorKey" />.
    /// </summary>
    Task<MobilePaymentView> StartAsync(StartMobilePayment request, CancellationToken cancellationToken);

    Task<MobilePaymentView?> FindAsync(Guid paymentId, CancellationToken cancellationToken);
}

/// <param name="Phone">E.164 Kenyan mobile that receives the prompt.</param>
/// <param name="AccountReference">Shown on the M-Pesa statement; at most 12 characters.</param>
/// <param name="Description">Shown on the PIN prompt; at most 13 characters.</param>
/// <param name="CallbackHost">Public host name of the tenant site the provider calls back on.</param>
/// <param name="DestinationId">
///     The till the money goes to (<see cref="IPaymentDestinations" />). Null: the platform account (tickets, D-02).
/// </param>
public sealed record StartMobilePayment(
    Guid PaymentId,
    PaymentPurpose Purpose,
    Guid ReferenceId,
    decimal Amount,
    string Phone,
    string AccountReference,
    string Description,
    string CallbackHost,
    Guid? DestinationId = null);

public enum MobilePaymentStatus
{
    /// <summary>The prompt is on the customer's phone (or being sent).</summary>
    Pending = 1,

    Succeeded = 2,

    Failed = 3,

    /// <summary>Paid, but something does not match (amount); Finance must review it.</summary>
    NeedsReview = 4
}

/// <param name="ErrorKey">Stable key when the prompt could not be sent, e.g. <c>Payments.ProviderBusy</c>.</param>
public sealed record MobilePaymentView(
    Guid PaymentId,
    MobilePaymentStatus Status,
    PaymentFailureReason? FailureReason,
    string? ErrorKey,
    string? MpesaReceipt,
    int? RetryAfterSeconds);
