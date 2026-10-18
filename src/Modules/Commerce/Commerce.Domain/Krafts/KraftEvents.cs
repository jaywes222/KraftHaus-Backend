using Commerce.Domain.Common;
using Common.Domain.Events;

namespace Commerce.Domain.Krafts;

public sealed record KraftCreated(KraftId KraftId, KraftDiscipline Discipline) : DomainEvent;

public sealed record KraftProfileUpdated(KraftId KraftId) : DomainEvent;

public sealed record KraftBrandAdded(KraftId KraftId, KraftBrandId BrandId) : DomainEvent;

public sealed record KraftBrandUpdated(KraftId KraftId, KraftBrandId BrandId) : DomainEvent;

public sealed record KraftBrandRemoved(KraftId KraftId, KraftBrandId BrandId) : DomainEvent;

public sealed record KrafterAdded(KraftId KraftId, KrafterId KrafterId) : DomainEvent;

public sealed record KrafterUpdated(KraftId KraftId, KrafterId KrafterId) : DomainEvent;

public sealed record KrafterRemoved(KraftId KraftId, KrafterId KrafterId) : DomainEvent;

public sealed record KraftJoinedEvent(KraftId KraftId, Guid EventId, decimal CommissionPercent) : DomainEvent;

public sealed record KraftEventTermsChanged(KraftId KraftId, Guid EventId, decimal CommissionPercent, bool CommissionChanged) : DomainEvent;

public sealed record KraftWithdrawnFromEvent(KraftId KraftId, Guid EventId) : DomainEvent;

public sealed record KraftArchived(KraftId KraftId) : DomainEvent;
