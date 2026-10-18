using Common.Domain.Events;
using Ticketing.Domain.Common;

namespace Ticketing.Domain.Orders;

public sealed record OrderPlaced(OrderId OrderId, Guid EventId, int Admissions, decimal Total) : DomainEvent;

public sealed record OrderPaymentStarted(OrderId OrderId, Guid PaymentId, decimal Amount) : DomainEvent;

public sealed record OrderPaymentFailed(OrderId OrderId, Guid PaymentId, string Reason) : DomainEvent;

public sealed record OrderPaid(OrderId OrderId, Guid PaymentId, bool Late) : DomainEvent;

public sealed record OrderExpired(OrderId OrderId) : DomainEvent;

/// <summary>Money arrived that the order cannot use (seats gone after a late payment, or a second payment). Finance refunds it.</summary>
public sealed record OrderNeedsAttention(OrderId OrderId, Guid PaymentId, string Reason) : DomainEvent;

/// <summary>Lipa Mdogo Mdogo deposit confirmed: seats are sold, tickets issued without QR until the balance is paid.</summary>
public sealed record OrderDepositPaid(OrderId OrderId, Guid PaymentId, decimal Amount, bool Late) : DomainEvent;

public sealed record OrderCancelled(OrderId OrderId, string Reason) : DomainEvent;

public sealed record OrderAttentionResolved(OrderId OrderId) : DomainEvent;
