using Common.Domain.ResultMonad;
using Common.IntegrationEvents;
using Payments.Domain.Common;
using Payments.Domain.Destinations;
using Payments.Domain.Payments;

namespace Payments.Application.Abstractions;

/// <summary>Payments in the resolved tenant's database.</summary>
public interface IPaymentRepository
{
    Task<Payment?> FindAsync(PaymentId id, CancellationToken cancellationToken);

    Task<Payment?> FindByCallbackTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    ///     Payments still waiting for an answer (Initiating, Pending, or Expired within the late window) whose last
    ///     check is older than <paramref name="checkedBefore" />, oldest first.
    /// </summary>
    Task<IReadOnlyList<Payment>> ListDueForCheckAsync(
        DateTimeOffset createdBefore,
        DateTimeOffset checkedBefore,
        DateTimeOffset expiredAfter,
        int max,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Payment>> ListForReadAsync(
        PaymentStatus? status,
        Guid? referenceId,
        DateTimeOffset? before,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Count and amount per status for payments created in <c>[from, to)</c>, plus the settled share of successes.</summary>
    Task<PaymentTotals> SummarizeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    void Add(Payment payment);
}

public sealed record PaymentStatusTotal(PaymentStatus Status, int Count, decimal Amount);

public sealed record PaymentTotals(IReadOnlyList<PaymentStatusTotal> ByStatus, int SettledCount, decimal SettledAmount);

/// <summary>Settlement records (BR-025), keyed by payment id.</summary>
public interface ISettlementRepository
{
    Task<SettlementRecord?> FindAsync(PaymentId paymentId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<PaymentId, SettlementRecord>> FindManyAsync(IReadOnlyCollection<PaymentId> paymentIds, CancellationToken cancellationToken);

    void Add(SettlementRecord record);
}

/// <summary>Tills (Spec 08 §7.1).</summary>
public interface IPaymentDestinationRepository
{
    Task<PaymentDestination?> FindAsync(PaymentDestinationId id, CancellationToken cancellationToken);

    /// <summary>The submitted or approved till of a Kraft at an event.</summary>
    Task<PaymentDestination?> FindActiveAsync(Guid kraftId, Guid eventId, CancellationToken cancellationToken);

    Task<PaymentDestination?> FindByTestPaymentAsync(Guid paymentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentDestination>> ListForKraftAsync(Guid kraftId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentDestination>> ListAsync(
        DestinationStatus? status,
        Guid? eventId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlySet<Guid>> KraftsWithApprovedTillAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>The KRAFTHAUS till that takes platform money now, if one is approved.</summary>
    Task<PaymentDestination?> FindApprovedPlatformTillAsync(CancellationToken cancellationToken);

    /// <summary>The KRAFTHAUS tills, newest first (the current one and its history).</summary>
    Task<IReadOnlyList<PaymentDestination>> ListPlatformTillsAsync(int limit, CancellationToken cancellationToken);

    void Add(PaymentDestination destination);
}

public interface IPaymentsUnitOfWork
{
    /// <summary>Queues an integration event in the Payments outbox; written by the next save, in the same transaction.</summary>
    void Publish(IntegrationEvent @event);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Saves; a lost optimistic-concurrency race or a duplicate active till comes back as a failure instead of throwing.</summary>
    Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken);

    Task<bool> AlreadyProcessedAsync(Guid messageId, string handler, CancellationToken cancellationToken);

    /// <summary>Records that <paramref name="handler" /> processed the message; written by the next save.</summary>
    void MarkProcessed(Guid messageId, string handler);
}
