using Common.Domain.StronglyTypedIds;

namespace Events.Domain.Common;

public readonly record struct EventId(Guid Value) : IStronglyTypedId;

public readonly record struct EventDateId(Guid Value) : IStronglyTypedId;

public readonly record struct TicketTierId(Guid Value) : IStronglyTypedId;

public readonly record struct SalePhaseId(Guid Value) : IStronglyTypedId;

public readonly record struct GroupOfferId(Guid Value) : IStronglyTypedId;

public readonly record struct EventEntranceId(Guid Value) : IStronglyTypedId;

public readonly record struct VenueId(Guid Value) : IStronglyTypedId;

public readonly record struct VenueEntranceId(Guid Value) : IStronglyTypedId;
