using Common.Application.EventCatalog;
using Common.Domain.ResultMonad;
using Displays.Application.Abstractions;
using Displays.Domain.Common;
using Displays.Domain.Programme;

namespace Displays.Application.Programme;

public sealed record ProgrammeSlotRequest(
    Guid? EventDateId,
    string? Title,
    string? Who,
    string? Stage,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public sealed record ProgrammeSlotView(
    Guid Id,
    Guid EventId,
    Guid? EventDateId,
    string Title,
    string? Who,
    string? Stage,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt)
{
    public static ProgrammeSlotView From(ProgrammeSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        return new ProgrammeSlotView(slot.Id.Value, slot.EventId, slot.EventDateId, slot.Title, slot.Who, slot.Stage, slot.Description, slot.StartsAt, slot.EndsAt);
    }
}

/// <summary>The event programme (what's on, when, where) that schedule slides show as "now" and "next".</summary>
public sealed class ProgrammeService(
    IProgrammeRepository slots,
    IDisplaysUnitOfWork unitOfWork,
    IDisplayRealtime realtime,
    IEventCatalog events,
    TimeProvider time)
{
    public async Task<Result<ProgrammeSlotView>> AddAsync(Guid eventId, ProgrammeSlotRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var checkedDate = await CheckDateAsync(eventId, request.EventDateId, cancellationToken);
        if (checkedDate.IsFailure)
        {
            return checkedDate.Error!;
        }

        var added = ProgrammeSlot.Add(new ProgrammeSlotId(Guid.CreateVersion7(time.GetUtcNow())), eventId, Spec(request));
        if (added.IsFailure)
        {
            return added.Error!;
        }

        slots.Add(added.Value!);

        return await SaveAsync(added.Value!, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<ProgrammeSlotView>>> ListAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var list = await slots.ListForEventAsync(eventId, cancellationToken);

        return Result<IReadOnlyList<ProgrammeSlotView>>.Success([.. list.Select(ProgrammeSlotView.From)]);
    }

    /// <summary>The published programme for the event page and app (draft events are not shown).</summary>
    public async Task<Result<IReadOnlyList<ProgrammeSlotView>>> PublicListAsync(string slug, CancellationToken cancellationToken)
    {
        var snapshot = await events.FindBySlugAsync(slug, cancellationToken);
        if (snapshot is null || snapshot.Status == EventLifecycle.Draft)
        {
            return DisplaysErrors.EventNotFound;
        }

        return await ListAsync(snapshot.EventId, cancellationToken);
    }

    public async Task<Result<ProgrammeSlotView>> UpdateAsync(Guid slotId, ProgrammeSlotRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var slot = await slots.FindAsync(new ProgrammeSlotId(slotId), cancellationToken);
        if (slot is null)
        {
            return DisplaysErrors.SlotNotFound;
        }

        var checkedDate = await CheckDateAsync(slot.EventId, request.EventDateId, cancellationToken);
        if (checkedDate.IsFailure)
        {
            return checkedDate.Error!;
        }

        var updated = slot.Update(Spec(request));

        return updated.IsFailure ? updated.Error! : await SaveAsync(slot, cancellationToken);
    }

    public async Task<Result<ProgrammeSlotView>> RemoveAsync(Guid slotId, CancellationToken cancellationToken)
    {
        var slot = await slots.FindAsync(new ProgrammeSlotId(slotId), cancellationToken);
        if (slot is null)
        {
            return DisplaysErrors.SlotNotFound;
        }

        slot.MarkRemoved();
        slots.Remove(slot);

        return await SaveAsync(slot, cancellationToken);
    }

    private static ProgrammeSlotSpec Spec(ProgrammeSlotRequest request) =>
        new(request.EventDateId, request.Title, request.Who, request.Stage, request.Description, request.StartsAt, request.EndsAt);

    private async Task<Result> CheckDateAsync(Guid eventId, Guid? eventDateId, CancellationToken cancellationToken)
    {
        var sale = eventId == Guid.Empty ? null : await events.GetSaleSnapshotAsync(eventId, cancellationToken);
        if (sale is null)
        {
            return DisplaysErrors.EventNotFound;
        }

        return eventDateId is { } dateId && dateId != Guid.Empty && sale.Dates.All(x => x.DateId != dateId)
            ? DisplaysErrors.Invalid("SlotInvalid", ["EventDateId: That date is not part of the event."])
            : Result.Success;
    }

    private async Task<Result<ProgrammeSlotView>> SaveAsync(ProgrammeSlot slot, CancellationToken cancellationToken)
    {
        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        await realtime.EventChangedAsync(slot.EventId, "programme", cancellationToken);

        return ProgrammeSlotView.From(slot);
    }
}
