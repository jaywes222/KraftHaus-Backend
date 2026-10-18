using Commerce.Domain.Orders;

namespace Commerce.Application.Orders;

/// <summary>
///     Pushes order changes to the Kraft's kitchen board and to the customer (Spec 08 §10, SignalR). Best effort:
///     clients re-fetch on reconnect and poll as a fallback, so a failed push never fails the change.
/// </summary>
public interface IKraftOrderRealtime
{
    Task OrderChangedAsync(KraftOrder order, CancellationToken cancellationToken);
}
