using Common.Domain.Events;
using Displays.Domain.Common;

namespace Displays.Domain.Programme;

public sealed record ProgrammeSlotAdded(ProgrammeSlotId SlotId, Guid EventId, DateTimeOffset StartsAt) : DomainEvent;

public sealed record ProgrammeSlotUpdated(ProgrammeSlotId SlotId, DateTimeOffset StartsAt) : DomainEvent;

public sealed record ProgrammeSlotRemoved(ProgrammeSlotId SlotId, Guid EventId) : DomainEvent;
