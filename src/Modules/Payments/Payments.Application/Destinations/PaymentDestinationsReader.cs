using Common.Application.Payments;
using Payments.Application.Abstractions;

namespace Payments.Application.Destinations;

/// <summary><see cref="IPaymentDestinations" /> for other modules (Commerce looks up a Kraft's approved till here).</summary>
public sealed class PaymentDestinationsReader(IPaymentDestinationRepository destinations) : IPaymentDestinations
{
    public async Task<Guid?> FindApprovedTillAsync(Guid kraftId, Guid eventId, CancellationToken cancellationToken)
    {
        var till = await destinations.FindActiveAsync(kraftId, eventId, cancellationToken);

        return till is { Status: Domain.Destinations.DestinationStatus.Approved } ? till.Id.Value : null;
    }

    public Task<IReadOnlySet<Guid>> KraftsWithApprovedTillAsync(Guid eventId, CancellationToken cancellationToken) =>
        destinations.KraftsWithApprovedTillAsync(eventId, cancellationToken);
}
