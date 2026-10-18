using Commerce.Application.Images;
using Commerce.Application.Stock;
using Commerce.Domain.Menus;

namespace Commerce.Application.Menus;

public sealed record MenuItemRequest(
    string? Name,
    string? Description,
    MenuCategory Category,
    decimal Price,
    int? LiveStock,
    int? ReservationAllocation,
    bool? AgeRestricted,
    int? SortOrder,
    IReadOnlyList<MenuAddOnSpec>? AddOns = null,
    DateTimeOffset? StartsAt = null,
    int? DurationMinutes = null)
{
    public MenuItemSpec ToSpec() =>
        new(Name, Description, Category, Price, LiveStock, ReservationAllocation, AgeRestricted, SortOrder ?? 0, AddOns, StartsAt, DurationMinutes);
}

public sealed record AddMenuItemRequest(
    Guid EventId,
    string? Name,
    string? Description,
    MenuCategory Category,
    decimal Price,
    int? LiveStock,
    int? ReservationAllocation,
    bool? AgeRestricted,
    int? SortOrder,
    IReadOnlyList<MenuAddOnSpec>? AddOns = null,
    DateTimeOffset? StartsAt = null,
    int? DurationMinutes = null)
{
    public MenuItemSpec ToSpec() =>
        new(Name, Description, Category, Price, LiveStock, ReservationAllocation, AgeRestricted, SortOrder ?? 0, AddOns, StartsAt, DurationMinutes);
}

public sealed record SetAvailabilityRequest(bool Available);

/// <param name="LiveLeft">Live portions still free; null for no limit.</param>
/// <param name="ReservationsLeft">Reservable portions still free; null when not reservable.</param>
public sealed record MenuItemAdminView(
    Guid Id,
    Guid KraftId,
    Guid EventId,
    string Name,
    string? Description,
    string Category,
    decimal Price,
    bool AgeRestricted,
    int SortOrder,
    bool Available,
    string Status,
    int? LiveStock,
    int LiveHeld,
    int LiveSold,
    int? LiveLeft,
    int? ReservationAllocation,
    int ReservationsHeld,
    int ReservationsSold,
    int? ReservationsLeft,
    IReadOnlyList<MenuAddOn> AddOns,
    DateTimeOffset? StartsAt,
    int? DurationMinutes,
    string? ImageHref)
{
    public static MenuItemAdminView From(MenuItem item, IReadOnlyList<StockLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(levels);

        var live = levels.FirstOrDefault(x => x.MenuItemId == item.Id.Value && x.Kind == StockKind.Live);
        var reserved = levels.FirstOrDefault(x => x.MenuItemId == item.Id.Value && x.Kind == StockKind.Reservation);

        return new MenuItemAdminView(
            item.Id.Value,
            item.KraftId,
            item.EventId,
            item.Name,
            item.Description,
            item.Category.ToString(),
            item.Price,
            item.AgeRestricted,
            item.SortOrder,
            item.Available,
            item.Status.ToString(),
            item.LiveStock,
            live?.Held ?? 0,
            live?.Sold ?? 0,
            item.LiveStock is { } stock ? Math.Max(0, stock - (live?.Held ?? 0) - (live?.Sold ?? 0)) : null,
            item.ReservationAllocation,
            reserved?.Held ?? 0,
            reserved?.Sold ?? 0,
            item.ReservationAllocation is { } allocation ? Math.Max(0, allocation - (reserved?.Held ?? 0) - (reserved?.Sold ?? 0)) : null,
            item.AddOns,
            item.StartsAt,
            item.DurationMinutes,
            ImageHrefOf(item));
    }

    /// <summary>The photo's public URL with its version, so a replaced photo is never served from cache.</summary>
    public static string? ImageHrefOf(MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return CommerceImageUrls.For(item.ImagePath);
    }
}

/// <summary>An event's public menu (UF-007), grouped by Kraft.</summary>
/// <param name="ReservationsOpen">Pre-orders are being taken now.</param>
/// <param name="LiveOrderingOpen">Live orders are being taken now (event day).</param>
public sealed record PublicMenuView(
    Guid EventId,
    string EventSlug,
    bool ReservationsOpen,
    DateTimeOffset? ReservationsCloseAt,
    bool LiveOrderingOpen,
    DateTimeOffset? LiveOrderingOpensAt,
    DateTimeOffset? LiveOrderingClosesAt,
    IReadOnlyList<PublicMenuKraftView> Krafts);

public sealed record PublicMenuKraftView(
    Guid KraftId,
    string Name,
    string Slug,
    string? Stall,
    IReadOnlyList<PublicMenuItemView> Items,
    string? LogoHref = null);

/// <param name="SoldOut">No live portions left (or switched off by the kitchen).</param>
/// <param name="ReservationsLeft">Null when the item is not reservable.</param>
public sealed record PublicMenuItemView(
    Guid Id,
    string Name,
    string? Description,
    string Category,
    decimal Price,
    bool AgeRestricted,
    bool SoldLive,
    bool SoldOut,
    int? LiveLeft,
    bool Reservable,
    int? ReservationsLeft,
    IReadOnlyList<MenuAddOn> AddOns,
    DateTimeOffset? StartsAt,
    int? DurationMinutes,
    string? ImageHref);
