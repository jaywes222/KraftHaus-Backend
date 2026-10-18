using Common.Domain.StronglyTypedIds;

namespace Commerce.Domain.Common;

public readonly record struct KraftId(Guid Value) : IStronglyTypedId;

public readonly record struct KraftBrandId(Guid Value) : IStronglyTypedId;

public readonly record struct KrafterId(Guid Value) : IStronglyTypedId;

public readonly record struct KraftParticipationId(Guid Value) : IStronglyTypedId;

public readonly record struct MenuItemId(Guid Value) : IStronglyTypedId;

public readonly record struct KraftOrderId(Guid Value) : IStronglyTypedId;

public readonly record struct KraftOrderLineId(Guid Value) : IStronglyTypedId;

public readonly record struct KraftSettlementId(Guid Value) : IStronglyTypedId;
