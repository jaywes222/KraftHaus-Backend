using System.Globalization;
using Common.Application.Auditing;
using Common.Domain.Aggregates;
using Common.Domain.Events;
using Music.Domain.Requests;

namespace Music.Infrastructure.Auditing;

/// <summary>Music domain events → audit actions (<c>music.board.*</c>, <c>music.request.*</c>). No names or songs-by-person.</summary>
public static class MusicAuditMapper
{
    public const string Module = "music";

    public static (string Action, AuditTarget Target, IReadOnlyDictionary<string, string> Metadata) Map(
        DomainEvent domainEvent,
        IAggregateRoot aggregate)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(aggregate);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        var (action, targetType) = domainEvent switch
        {
            RequestBoardOpened opened => (Board("music.board.opened", opened.EventId, opened.MaxPerPerson), "request_board"),
            RequestBoardClosed closed => (Board("music.board.closed", closed.EventId, null), "request_board"),
            RequestBoardUpdated updated => (Board("music.board.updated", updated.EventId, updated.MaxPerPerson), "request_board"),
            SongRequested requested => (Requested(requested), "song_request"),
            SongRequestSeconded seconded => (Seconded(seconded), "song_request"),
            SongRequestHandled handled => (Handled(handled), "song_request"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(domainEvent),
                domainEvent.GetType().Name,
                "Every Music domain event needs a stable audit action.")
        };

        return (action, new AuditTarget(targetType, aggregate.Id.Value.ToString("D", CultureInfo.InvariantCulture)), metadata);

        string Board(string name, Guid eventId, int? maxPerPerson)
        {
            metadata["eventId"] = Id(eventId);
            if (maxPerPerson is { } max)
            {
                metadata["maxPerPerson"] = max.ToString(CultureInfo.InvariantCulture);
            }

            return name;
        }

        string Requested(SongRequested requested)
        {
            metadata["eventId"] = Id(requested.EventId);
            metadata["source"] = requested.Source.ToString();
            return "music.request.created";
        }

        string Seconded(SongRequestSeconded seconded)
        {
            metadata["eventId"] = Id(seconded.EventId);
            metadata["votes"] = seconded.Votes.ToString(CultureInfo.InvariantCulture);
            return "music.request.seconded";
        }

        string Handled(SongRequestHandled handled)
        {
            metadata["eventId"] = Id(handled.EventId);
            metadata["from"] = handled.From.ToString();
            metadata["to"] = handled.To.ToString();
            return "music.request.status_changed";
        }
    }

    private static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);
}
