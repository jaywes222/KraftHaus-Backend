using Common.Domain.Events;
using Displays.Domain.Common;

namespace Displays.Domain.Configurations;

public sealed record DisplayConfigurationCreated(DisplayConfigurationId ConfigurationId, Guid EventId, int Slides) : DomainEvent;

public sealed record DisplayConfigurationUpdated(DisplayConfigurationId ConfigurationId, int Slides) : DomainEvent;

public sealed record DisplayConfigurationArchived(DisplayConfigurationId ConfigurationId) : DomainEvent;
