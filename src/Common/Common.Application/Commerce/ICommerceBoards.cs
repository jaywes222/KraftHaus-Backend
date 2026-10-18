namespace Common.Application.Commerce;

/// <summary>
///     What display screens show from Commerce (implemented by Commerce): the menu of an event and the orders ready
///     for collection. Krafts without an approved till for the event are left out, like on the customer menu.
/// </summary>
public interface ICommerceBoards
{
    /// <param name="kraftIds">Only these Krafts; empty for every Kraft at the event.</param>
    Task<IReadOnlyList<MenuBoardKraft>> GetMenuAsync(Guid eventId, IReadOnlyCollection<Guid> kraftIds, CancellationToken cancellationToken);

    /// <summary>Orders waiting at the stall, oldest first, at most <paramref name="limit" />.</summary>
    Task<IReadOnlyList<ReadyOrder>> GetReadyOrdersAsync(
        Guid eventId,
        IReadOnlyCollection<Guid> kraftIds,
        int limit,
        CancellationToken cancellationToken);
}

public sealed record MenuBoardKraft(Guid KraftId, string Name, string? Stall, IReadOnlyList<MenuBoardItem> Items);

/// <param name="SoldOut">No live portions left, or switched off by the kitchen.</param>
public sealed record MenuBoardItem(Guid Id, string Name, string Category, decimal Price, bool AgeRestricted, bool SoldOut);

/// <param name="Reference">The order number on the customer's receipt; no names or phone digits (public screen).</param>
public sealed record ReadyOrder(string Reference, Guid KraftId, string KraftName, string? Stall, DateTimeOffset ReadyAt);
