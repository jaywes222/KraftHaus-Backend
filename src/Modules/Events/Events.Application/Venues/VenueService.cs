using Common.Domain.ResultMonad;
using Events.Application.Abstractions;
using Events.Domain.Common;
using Events.Domain.Venues;

namespace Events.Application.Venues;

/// <summary>The tenant's reusable venue library: list, read, create, edit, manage entrances, archive.</summary>
public sealed class VenueService(
    IVenueRepository venues,
    IEventsUnitOfWork unitOfWork,
    TimeProvider time)
{
    public async Task<Result<IReadOnlyList<VenueView>>> ListAsync(
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var list = await venues.ListForReadAsync(includeArchived, cancellationToken);

        return Result<IReadOnlyList<VenueView>>.Success([.. list.Select(VenueView.From)]);
    }

    public async Task<Result<VenueView>> GetAsync(VenueId id, CancellationToken cancellationToken)
    {
        var venue = await venues.FindForReadAsync(id, cancellationToken);

        return venue is null
            ? EventsErrors.NotFound("Venue", id.Value)
            : VenueView.From(venue);
    }

    public async Task<Result<VenueView>> CreateAsync(VenueInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var details = VenueDetails.Create(input.Name, input.AddressLine, input.City, input.MapUrl, input.Directions);
        if (details.IsFailure)
        {
            return details.Error!;
        }

        var venue = Venue.Create(new VenueId(NewGuid()), details.Value!, input.Capacity);
        if (venue.IsFailure)
        {
            return venue.Error!;
        }

        venues.Add(venue.Value!);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return VenueView.From(venue.Value!);
    }

    public Task<Result<VenueView>> UpdateAsync(VenueId id, VenueInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ChangeAsync(
            id,
            venue =>
            {
                var details = VenueDetails.Create(
                    input.Name,
                    input.AddressLine,
                    input.City,
                    input.MapUrl,
                    input.Directions);

                return details.IsFailure
                    ? details.Error!
                    : venue.Update(details.Value!, input.Capacity);
            },
            cancellationToken);
    }

    public Task<Result<VenueView>> AddEntranceAsync(
        VenueId id,
        EntranceInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ChangeAsync(
            id,
            venue => venue.AddEntrance(new VenueEntranceId(NewGuid()), input.Name, input.Description),
            cancellationToken);
    }

    public Task<Result<VenueView>> UpdateEntranceAsync(
        VenueId id,
        VenueEntranceId entranceId,
        EntranceInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ChangeAsync(
            id,
            venue => venue.UpdateEntrance(entranceId, input.Name, input.Description),
            cancellationToken);
    }

    public Task<Result<VenueView>> RemoveEntranceAsync(
        VenueId id,
        VenueEntranceId entranceId,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, venue => venue.RemoveEntrance(entranceId), cancellationToken);

    public Task<Result<VenueView>> ArchiveAsync(VenueId id, CancellationToken cancellationToken) =>
        ChangeAsync(id, venue => venue.Archive(), cancellationToken);

    private async Task<Result<VenueView>> ChangeAsync(
        VenueId id,
        Func<Venue, Result> change,
        CancellationToken cancellationToken)
    {
        var venue = await venues.FindAsync(id, cancellationToken);
        if (venue is null)
        {
            return EventsErrors.NotFound("Venue", id.Value);
        }

        var result = change(venue);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return VenueView.From(venue);
    }

    private Guid NewGuid() => Guid.CreateVersion7(time.GetUtcNow());
}
