using Common.Domain.Events;
using Common.IntegrationEvents.Payments;
using Payments.Domain.Common;

namespace Payments.Domain.Payments;

public sealed record PaymentCreated(PaymentId PaymentId, PaymentPurpose Purpose, Guid ReferenceId, decimal Amount) : DomainEvent;

public sealed record PaymentPromptSent(PaymentId PaymentId) : DomainEvent;

public sealed record PaymentPromptNotSent(PaymentId PaymentId, string? ProviderErrorCode) : DomainEvent;

public sealed record PaymentConfirmed(PaymentId PaymentId, bool Late) : DomainEvent;

public sealed record PaymentNotCompleted(PaymentId PaymentId, PaymentStatus Status, string? ResultCode) : DomainEvent;

/// <summary>The provider reports success but the amount differs from what was asked: Finance must review.</summary>
public sealed record PaymentFlaggedForReview(PaymentId PaymentId, decimal ExpectedAmount, decimal ReportedAmount) : DomainEvent;

public sealed record PaymentReviewed(PaymentId PaymentId, PaymentReviewDecision Decision) : DomainEvent;
