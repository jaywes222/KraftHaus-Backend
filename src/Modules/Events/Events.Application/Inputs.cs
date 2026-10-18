using Events.Domain.Model;

namespace Events.Application;

// Request bodies of the admin endpoints. Prices are whole Kenya shillings (KES).

public sealed record VenueInput(
    string? Name,
    string? AddressLine,
    string? City,
    Uri? MapUrl,
    string? Directions,
    int? Capacity);

public sealed record EntranceInput(string? Name, string? Description);

public sealed record EventDetailsInput(
    string? Slug,
    string? Title,
    string? Summary,
    string? Description,
    Uri? PosterUrl);

public sealed record PoliciesInput(
    bool RefundsEnabled,
    bool TransfersEnabled,
    int MaxTransfersPerTicket,
    bool LipaMdogoMdogoEnabled,
    int HoldMinutes,
    int MaxTicketsPerOrder);

public sealed record UseVenueInput(Guid VenueId);

public sealed record InlineVenueInput(
    string? Name,
    string? AddressLine,
    string? City,
    Uri? MapUrl,
    string? Directions);

public sealed record DateInput(
    string? Label,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset? DoorsOpenAt);

public sealed record TierInput(string? Name, string? Description, int Capacity);

public sealed record PhaseInput(
    string? Name,
    decimal Price,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Allocation,
    bool EarlyAccess = false);

public sealed record GroupOfferInput(
    GroupOfferKind Kind,
    string? Name,
    int Admits,
    decimal Price,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Allocation);

public sealed record CancelEventInput(string? Reason);
