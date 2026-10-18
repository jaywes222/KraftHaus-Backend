using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Displays.Domain.Common;

namespace Displays.Domain.Programme;

/// <param name="Who">Performer, speaker or Kraft ("DJ Kace", "Mama Pima").</param>
/// <param name="Stage">Where it happens ("Courtyard").</param>
public sealed record ProgrammeSlotSpec(
    Guid? EventDateId,
    string? Title,
    string? Who,
    string? Stage,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

/// <summary>One item of an event's programme, shown on schedule slides as "now" and "next".</summary>
public sealed class ProgrammeSlot : AggregateRoot<ProgrammeSlotId>
{
    public const int TitleMaxLength = 80;
    public const int WhoMaxLength = 80;
    public const int StageMaxLength = 40;
    public const int DescriptionMaxLength = 300;
    public static readonly TimeSpan MaxLength = TimeSpan.FromHours(24);

    private ProgrammeSlot(ProgrammeSlotId id)
        : base(id)
    {
    }

    public Guid EventId { get; private set; }

    public Guid? EventDateId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Who { get; private set; }

    public string? Stage { get; private set; }

    public string? Description { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public static Result<ProgrammeSlot> Add(ProgrammeSlotId id, Guid eventId, ProgrammeSlotSpec spec)
    {
        var slot = new ProgrammeSlot(id) { EventId = eventId };
        var applied = slot.Apply(spec);
        if (applied.IsFailure)
        {
            return applied.Error!;
        }

        slot.RaiseEvent(new ProgrammeSlotAdded(id, eventId, slot.StartsAt));

        return slot;
    }

    public Result Update(ProgrammeSlotSpec spec)
    {
        var applied = Apply(spec);
        if (applied.IsFailure)
        {
            return applied;
        }

        RaiseEvent(new ProgrammeSlotUpdated(Id, StartsAt));

        return Result.Success;
    }

    public void MarkRemoved() => RaiseEvent(new ProgrammeSlotRemoved(Id, EventId));

    private Result Apply(ProgrammeSlotSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var errors = new List<string>();
        var title = spec.Title?.Trim() ?? string.Empty;
        var who = string.IsNullOrWhiteSpace(spec.Who) ? null : spec.Who.Trim();
        var stage = string.IsNullOrWhiteSpace(spec.Stage) ? null : spec.Stage.Trim();
        var description = string.IsNullOrWhiteSpace(spec.Description) ? null : spec.Description.Trim();

        if (title.Length is < 2 or > TitleMaxLength)
        {
            errors.Add($"Title: Use 2-{TitleMaxLength} characters.");
        }

        if (who is { Length: > WhoMaxLength })
        {
            errors.Add($"Who: Keep it under {WhoMaxLength} characters.");
        }

        if (stage is { Length: > StageMaxLength })
        {
            errors.Add($"Stage: Keep it under {StageMaxLength} characters.");
        }

        if (description is { Length: > DescriptionMaxLength })
        {
            errors.Add($"Description: Keep it under {DescriptionMaxLength} characters.");
        }

        if (spec.EndsAt <= spec.StartsAt)
        {
            errors.Add("EndsAt: Must be after the start.");
        }
        else if (spec.EndsAt - spec.StartsAt > MaxLength)
        {
            errors.Add("EndsAt: A slot can last at most 24 hours.");
        }

        if (spec.EventDateId == Guid.Empty)
        {
            errors.Add("EventDateId: Choose a date of the event or leave it empty.");
        }

        if (errors.Count > 0)
        {
            return DisplaysErrors.Invalid("SlotInvalid", errors);
        }

        EventDateId = spec.EventDateId;
        Title = title;
        Who = who;
        Stage = stage;
        Description = description;
        StartsAt = spec.StartsAt;
        EndsAt = spec.EndsAt;

        return Result.Success;
    }
}
