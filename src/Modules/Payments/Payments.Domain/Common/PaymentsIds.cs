using Common.Domain.StronglyTypedIds;

namespace Payments.Domain.Common;

public readonly record struct PaymentId(Guid Value) : IStronglyTypedId;

public readonly record struct PaymentDestinationId(Guid Value) : IStronglyTypedId;
