using Commerce.Domain.Common;
using Common.Domain.Events;

namespace Commerce.Domain.Orders;

public sealed record KraftOrderPlaced(KraftOrderId OrderId, Guid KraftId, Guid EventId, KraftOrderKind Kind, decimal Total, int Portions) : DomainEvent;

public sealed record KraftOrderPaymentStarted(KraftOrderId OrderId, Guid PaymentId) : DomainEvent;

public sealed record KraftOrderPaymentFailed(KraftOrderId OrderId, Guid PaymentId, string Reason) : DomainEvent;

public sealed record KraftOrderPaid(KraftOrderId OrderId, Guid PaymentId, decimal Total, decimal PlatformFee, bool Late) : DomainEvent;

public sealed record KraftOrderExpired(KraftOrderId OrderId, DateTimeOffset At) : DomainEvent;

public sealed record KraftOrderNeedsAttention(KraftOrderId OrderId, Guid PaymentId, string Reason) : DomainEvent;

public sealed record KraftOrderPreparing(KraftOrderId OrderId) : DomainEvent;

public sealed record KraftOrderReady(KraftOrderId OrderId) : DomainEvent;

public sealed record KraftOrderCollected(KraftOrderId OrderId) : DomainEvent;

public sealed record KraftOrderLineUnavailable(KraftOrderId OrderId, KraftOrderLineId LineId, decimal Amount) : DomainEvent;

public sealed record KraftOrderUncollected(KraftOrderId OrderId) : DomainEvent;

public sealed record KraftOrderAttentionResolved(KraftOrderId OrderId) : DomainEvent;
