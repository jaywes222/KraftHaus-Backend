using Common.Domain.Events;
using Payments.Domain.Common;

namespace Payments.Domain.Destinations;

public sealed record TillSubmitted(PaymentDestinationId DestinationId, Guid KraftId, Guid EventId) : DomainEvent;

public sealed record TillTestStarted(PaymentDestinationId DestinationId, Guid PaymentId) : DomainEvent;

public sealed record TillTestFinished(PaymentDestinationId DestinationId, Guid PaymentId, bool Passed) : DomainEvent;

public sealed record TillApproved(PaymentDestinationId DestinationId, Guid KraftId, Guid EventId) : DomainEvent;

public sealed record TillRejected(PaymentDestinationId DestinationId) : DomainEvent;

public sealed record TillDisabled(PaymentDestinationId DestinationId) : DomainEvent;
