using Common.Domain.ResultMonad;
using Events.Domain.Common;

namespace Events.Domain.Venues;

/// <summary>
///     Where an event happens: the descriptive part of a venue. Used by the reusable <see cref="Venue" /> and by the
///     copy an event keeps (so editing a venue on one event never changes another event or the library entry).
/// </summary>
public sealed record VenueDetails
{
    public const int NameMaxLength = 120;
    public const int AddressMaxLength = 200;
    public const int CityMaxLength = 80;
    public const int DirectionsMaxLength = 1000;

    // EF Core materialisation.
    private VenueDetails()
    {
    }

    public string Name { get; private init; } = string.Empty;

    public string? AddressLine { get; private init; }

    public string? City { get; private init; }

    public Uri? MapUrl { get; private init; }

    public string? Directions { get; private init; }

    public static Result<VenueDetails> Create(
        string? name,
        string? addressLine,
        string? city,
        Uri? mapUrl,
        string? directions)
    {
        var validName = Text.Required(name, "VenueName", NameMaxLength);
        if (validName.IsFailure)
        {
            return validName.Error!;
        }

        var validAddress = Text.Optional(addressLine, "VenueAddress", AddressMaxLength);
        if (validAddress.IsFailure)
        {
            return validAddress.Error!;
        }

        var validCity = Text.Optional(city, "VenueCity", CityMaxLength);
        if (validCity.IsFailure)
        {
            return validCity.Error!;
        }

        var validMap = Text.OptionalAbsoluteUrl(mapUrl, "VenueMapUrl");
        if (validMap.IsFailure)
        {
            return validMap.Error!;
        }

        var validDirections = Text.Optional(directions, "VenueDirections", DirectionsMaxLength);
        if (validDirections.IsFailure)
        {
            return validDirections.Error!;
        }

        return new VenueDetails
        {
            Name = validName.Value!,
            AddressLine = validAddress.Value,
            City = validCity.Value,
            MapUrl = validMap.Value,
            Directions = validDirections.Value
        };
    }
}
