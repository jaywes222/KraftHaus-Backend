using Common.Domain.StronglyTypedIds;

namespace Music.Domain.Common;

public readonly record struct RequestBoardId(Guid Value) : IStronglyTypedId;

public readonly record struct SongRequestId(Guid Value) : IStronglyTypedId;

public readonly record struct SongRequestVoteId(Guid Value) : IStronglyTypedId;
