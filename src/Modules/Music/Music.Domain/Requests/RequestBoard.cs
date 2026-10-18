using Common.Domain.Aggregates;
using Common.Domain.Events;
using Common.Domain.ResultMonad;
using Music.Domain.Common;

namespace Music.Domain.Requests;

/// <summary>
///     Whether an event takes song requests right now, and how many each ticket holder may make. One per event,
///     created closed the first time the DJ console opens the event.
/// </summary>
public sealed class RequestBoard : AggregateRoot<RequestBoardId>
{
    public const int MinPerPerson = 1;
    public const int MaxPerPersonLimit = 20;
    public const int NoteMaxLength = 140;

    private RequestBoard(RequestBoardId id)
        : base(id)
    {
    }

    public Guid EventId { get; private set; }

    public bool IsOpen { get; private set; }

    /// <summary>Requests one person may make for this event (a vote for a song already listed counts as one).</summary>
    public int MaxPerPerson { get; private set; }

    /// <summary>Shown to customers, e.g. "DJ Kace takes requests until 23:00".</summary>
    public string? Note { get; private set; }

    public DateTimeOffset? OpenedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public static RequestBoard Create(RequestBoardId id, Guid eventId, int maxPerPerson) =>
        new(id)
        {
            EventId = eventId,
            MaxPerPerson = Math.Clamp(maxPerPerson, MinPerPerson, MaxPerPersonLimit)
        };

    /// <summary>Sets whether requests are open, the per-person limit and the note in one go (the console's form).</summary>
    public Result Configure(bool open, int maxPerPerson, string? note, DateTimeOffset now)
    {
        var errors = new List<string>();
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (maxPerPerson is < MinPerPerson or > MaxPerPersonLimit)
        {
            errors.Add($"MaxPerPerson: Choose {MinPerPerson}-{MaxPerPersonLimit}.");
        }

        if (trimmed is { Length: > NoteMaxLength })
        {
            errors.Add($"Note: Keep it under {NoteMaxLength} characters.");
        }

        if (errors.Count > 0)
        {
            return MusicErrors.Invalid("BoardInvalid", errors);
        }

        var opening = open && !IsOpen;
        var closing = !open && IsOpen;
        var changed = maxPerPerson != MaxPerPerson || trimmed != Note;

        if (!opening && !closing && !changed)
        {
            return Result.Success;
        }

        DomainEvent @event;
        if (opening)
        {
            @event = new RequestBoardOpened(Id, EventId, maxPerPerson);
        }
        else if (closing)
        {
            @event = new RequestBoardClosed(Id, EventId);
        }
        else
        {
            @event = new RequestBoardUpdated(Id, EventId, maxPerPerson);
        }

        IsOpen = open;
        MaxPerPerson = maxPerPerson;
        Note = trimmed;
        if (opening)
        {
            OpenedAt = now;
        }

        if (closing)
        {
            ClosedAt = now;
        }

        RaiseEvent(@event);

        return Result.Success;
    }
}
