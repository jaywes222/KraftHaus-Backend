using Commerce.Domain.Common;
using Common.Domain.Events;

namespace Commerce.Domain.Menus;

public sealed record MenuItemAdded(MenuItemId ItemId, Guid KraftId, Guid EventId, decimal Price) : DomainEvent;

public sealed record MenuItemUpdated(MenuItemId ItemId, decimal OldPrice, decimal NewPrice) : DomainEvent;

public sealed record MenuItemAvailabilityChanged(MenuItemId ItemId, bool Available) : DomainEvent;

public sealed record MenuItemRetired(MenuItemId ItemId) : DomainEvent;

public sealed record MenuItemImageChanged(MenuItemId ItemId) : DomainEvent;
