using Events.Domain.Common;
using Events.Domain.Venues;

namespace Events.Domain.Model;

/// <summary>
///     The event's own copy of its venue. <see cref="SourceVenueId" /> records which library venue it was copied
///     from (null when typed inline); edits change only this copy.
/// </summary>
public sealed record EventVenue
{
    private EventVenue()
    {
    }

    public VenueId? SourceVenueId { get; private init; }

    public VenueDetails Details { get; private init; } = null!;

    internal static EventVenue From(VenueId? sourceVenueId, VenueDetails details) =>
        new()
        {
            SourceVenueId = sourceVenueId,

            // Own copy: EF Core tracks owned values per owner, so the library venue's instance is never shared.
            Details = details with { }
        };

    internal EventVenue WithDetails(VenueDetails details) => this with { Details = details with { } };
}
