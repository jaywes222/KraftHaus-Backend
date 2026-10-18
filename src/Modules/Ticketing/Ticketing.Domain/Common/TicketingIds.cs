using Common.Domain.StronglyTypedIds;

namespace Ticketing.Domain.Common;

public readonly record struct WaitlistEntryId(Guid Value) : IStronglyTypedId;

public readonly record struct OrderId(Guid Value) : IStronglyTypedId;

public readonly record struct OrderLineId(Guid Value) : IStronglyTypedId;

public readonly record struct TicketId(Guid Value) : IStronglyTypedId;
