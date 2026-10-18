using Common.Domain.StronglyTypedIds;

namespace Communications.Domain.Common;

public readonly record struct MessageId(Guid Value) : IStronglyTypedId;

public readonly record struct BroadcastId(Guid Value) : IStronglyTypedId;

public readonly record struct OptOutId(Guid Value) : IStronglyTypedId;

public readonly record struct MessageTemplateId(Guid Value) : IStronglyTypedId;
