using System.Security.Cryptography;
using Common.Application.Payments;
using Common.IntegrationEvents.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application.Abstractions;
using Payments.Application.Provider;
using Payments.Domain.Common;
using Payments.Domain.Destinations;
using Payments.Domain.Payments;

namespace Payments.Application.Payments;

/// <summary><see cref="IMobilePayments" /> for other modules: create the payment, then ask PalPluss for the prompt.</summary>
public sealed partial class MobilePaymentsService(
    IPaymentRepository payments,
    IPaymentDestinationRepository destinations,
    IPaymentsUnitOfWork unitOfWork,
    IPaymentProvider provider,
    IOptions<PaymentsOptions> options,
    TimeProvider time,
    ILogger<MobilePaymentsService> logger)
    : IMobilePayments
{
    public async Task<MobilePaymentView> StartAsync(StartMobilePayment request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = new PaymentId(request.PaymentId);

        var existing = await payments.FindAsync(id, cancellationToken);
        if (existing is not null)
        {
            return existing.ToMobileView();
        }

        Guid? channelId = null;

        if (request.DestinationId is { } destinationId)
        {
            var destination = await destinations.FindAsync(new PaymentDestinationId(destinationId), cancellationToken);
            var usable = destination is not null && (request.Purpose == PaymentPurpose.TillTest
                ? destination.IsActive && destination.TestPaymentId == request.PaymentId
                : destination.Status == DestinationStatus.Approved);

            if (!usable || destination!.ProviderChannelId is null)
            {
                // The till was disabled (or never approved) between the caller's check and now. Nothing is sent.
                return new MobilePaymentView(request.PaymentId, MobilePaymentStatus.Failed, PaymentFailureReason.NotSent,
                    "Payments.TillUnavailable", null, null);
            }

            channelId = destination.ProviderChannelId;
        }
        else if (request.Purpose != PaymentPurpose.TillTest &&
                 await destinations.FindApprovedPlatformTillAsync(cancellationToken) is { ProviderChannelId: { } platformChannel })
        {
            // Ticket money and food sold through the platform till go to the KRAFTHAUS till set in admin. Without an
            // approved one, PalPluss uses the configured channel or the account default.
            channelId = platformChannel;
        }

        var now = time.GetUtcNow();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var created = Payment.Create(
            id,
            request.Purpose,
            request.ReferenceId,
            request.Amount,
            request.Phone,
            request.AccountReference,
            request.Description,
            token,
            now,
            request.DestinationId,
            channelId);

        if (created.IsFailure)
        {
            // A programming error in the caller (amounts and references are computed, not typed).
            throw new ArgumentException(created.Error!.Description, nameof(request));
        }

        var payment = created.Value!;
        payments.Add(payment);

        // Saved before calling the provider: the callback (with this token) may arrive before the response does.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var settings = options.Value;
        var host = string.IsNullOrWhiteSpace(settings.CallbackHostOverride) ? request.CallbackHost : settings.CallbackHostOverride;
        var callback = new Uri($"{settings.CallbackScheme}://{host}/webhooks/palpluss/{token}");

        var result = await provider.StartStkAsync(
            new ProviderStkRequest(payment.Amount, payment.Phone, payment.AccountReference, payment.Description, callback, payment.ProviderChannelId),
            CancellationToken.None);

        var retryAfter = (int?)null;

        switch (result)
        {
            case ProviderStartResult.Accepted accepted:
                payment.MarkPromptSent(accepted.TransactionId, accepted.CheckoutId, time.GetUtcNow());
                break;

            case ProviderStartResult.Refused refused:
                payment.MarkNotSent(refused.ErrorCode, refused.Message, time.GetUtcNow());
                retryAfter = refused.RetryAfterSeconds;
                LogPromptRefused(logger, payment.Id.Value, refused.ErrorCode);
                break;

            case ProviderStartResult.Unknown unknown:
                // Stays Initiating: the callback can still adopt it; the status job expires it otherwise.
                LogPromptUnknown(logger, payment.Id.Value, unknown.Reason);
                break;
        }

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        return payment.ToMobileView(retryAfter);
    }

    public async Task<MobilePaymentView?> FindAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await payments.FindAsync(new PaymentId(paymentId), cancellationToken);
        return payment?.ToMobileView();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "PalPluss refused the prompt for payment {PaymentId}: {ErrorCode}.")]
    private static partial void LogPromptRefused(ILogger logger, Guid paymentId, string errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No answer from PalPluss for payment {PaymentId} ({Reason}); waiting for a callback.")]
    private static partial void LogPromptUnknown(ILogger logger, Guid paymentId, string reason);
}

internal static class PaymentMapping
{
    public static MobilePaymentView ToMobileView(this Payment payment, int? retryAfterSeconds = null) =>
        new(
            payment.Id.Value,
            payment.Status switch
            {
                PaymentStatus.Initiating or PaymentStatus.Pending => MobilePaymentStatus.Pending,
                PaymentStatus.Succeeded => MobilePaymentStatus.Succeeded,
                PaymentStatus.NeedsReview => MobilePaymentStatus.NeedsReview,
                _ => MobilePaymentStatus.Failed
            },
            payment.FailureReason,
            payment.Status == PaymentStatus.NotSent ? ErrorKeyFor(payment.ProviderErrorCode) : null,
            payment.MpesaReceipt,
            retryAfterSeconds);

    /// <summary>Stable keys for the client; provider codes stay internal.</summary>
    private static string ErrorKeyFor(string? providerCode) =>
        providerCode switch
        {
            "RATE_LIMIT_EXCEEDED" or "STK_TEMP_BANNED" or "TRANSACTION_HEALTH_BLOCKED" => "Payments.ProviderBusy",
            "INVALID_PHONE" => "Payments.PhoneNotSupported",
            _ => "Payments.ProviderUnavailable"
        };
}
