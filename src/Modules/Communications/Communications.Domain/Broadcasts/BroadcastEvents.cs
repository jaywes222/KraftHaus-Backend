using Common.Domain.Communications;
using Common.Domain.Events;
using Communications.Domain.Common;

namespace Communications.Domain.Broadcasts;

public sealed record BroadcastCreated(BroadcastId BroadcastId, Guid EventId, BroadcastAudience Audience) : DomainEvent;

public sealed record BroadcastUpdated(BroadcastId BroadcastId, Guid EventId, BroadcastAudience Audience) : DomainEvent;

public sealed record BroadcastScheduled(BroadcastId BroadcastId, Guid EventId, DateTimeOffset SendAt) : DomainEvent;

public sealed record BroadcastCancelled(BroadcastId BroadcastId, Guid EventId) : DomainEvent;

public sealed record BroadcastStarted(BroadcastId BroadcastId, Guid EventId, int Recipients, int Skipped) : DomainEvent;

public sealed record BroadcastCompleted(BroadcastId BroadcastId, Guid EventId, int Sent, int Failed) : DomainEvent;
