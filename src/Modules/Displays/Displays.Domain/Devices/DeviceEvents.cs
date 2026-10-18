using Common.Domain.Events;
using Displays.Domain.Common;

namespace Displays.Domain.Devices;

public sealed record DisplayDevicePaired(DisplayDeviceId DeviceId, Guid EventId, Guid? DefaultConfigurationId) : DomainEvent;

public sealed record DisplayDeviceUpdated(DisplayDeviceId DeviceId, Guid EventId, Guid? DefaultConfigurationId) : DomainEvent;

public sealed record DisplayScheduleSet(DisplayDeviceId DeviceId, int Entries) : DomainEvent;

public sealed record DisplayDeviceRevoked(DisplayDeviceId DeviceId, string Reason) : DomainEvent;
