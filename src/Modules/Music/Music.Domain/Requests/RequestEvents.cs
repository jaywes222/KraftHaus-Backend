using Common.Domain.Events;
using Music.Domain.Common;

namespace Music.Domain.Requests;

public sealed record RequestBoardOpened(RequestBoardId BoardId, Guid EventId, int MaxPerPerson) : DomainEvent;

public sealed record RequestBoardClosed(RequestBoardId BoardId, Guid EventId) : DomainEvent;

public sealed record RequestBoardUpdated(RequestBoardId BoardId, Guid EventId, int MaxPerPerson) : DomainEvent;

/// <summary>A first fan asked for a song. Personal data stays out of the event (audit metadata is sanitised too).</summary>
public sealed record SongRequested(SongRequestId RequestId, Guid EventId, TrackSource Source) : DomainEvent;

/// <summary>Another fan asked for a song already on the list.</summary>
public sealed record SongRequestSeconded(SongRequestId RequestId, Guid EventId, int Votes) : DomainEvent;

public sealed record SongRequestHandled(SongRequestId RequestId, Guid EventId, SongRequestStatus From, SongRequestStatus To) : DomainEvent;
