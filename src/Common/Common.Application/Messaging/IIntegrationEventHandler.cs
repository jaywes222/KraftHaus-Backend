using Common.IntegrationEvents;

namespace Common.Application.Messaging;

/// <summary>
///     Handles an integration event delivered by the tenant outbox dispatcher (Spec 08 §9). Delivery is
///     at-least-once: a handler must be idempotent, typically by recording the message id in its module's
///     <c>processed_message</c> table in the same transaction as its change. Runs inside the producing tenant's scope.
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
