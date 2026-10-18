using Common.IntegrationEvents.Payments;
using Payments.Application.Abstractions;
using Payments.Domain.Payments;

namespace Payments.Application.Payments;

/// <summary>Turns a payment transition into the integration event the owning module listens for (once per transition).</summary>
internal static class PaymentEventPublisher
{
    public static bool Publish(IPaymentsUnitOfWork unitOfWork, Payment payment, PaymentTransition transition)
    {
        switch (transition)
        {
            case PaymentTransition.Succeeded or PaymentTransition.SucceededLate:
                unitOfWork.Publish(new PaymentSucceeded(
                    payment.Id.Value,
                    payment.Purpose,
                    payment.ReferenceId,
                    payment.Amount,
                    payment.Currency,
                    payment.MpesaReceipt,
                    payment.CompletedAt ?? payment.CreatedAt,
                    transition == PaymentTransition.SucceededLate));
                return true;

            case PaymentTransition.Failed:
                unitOfWork.Publish(new PaymentFailed(
                    payment.Id.Value,
                    payment.Purpose,
                    payment.ReferenceId,
                    payment.FailureReason ?? PaymentFailureReason.Declined,
                    payment.ResultDescription));
                return true;

            default:
                // FlaggedForReview: money arrived but does not match. Finance resolves it; the order is not fulfilled.
                return false;
        }
    }
}
