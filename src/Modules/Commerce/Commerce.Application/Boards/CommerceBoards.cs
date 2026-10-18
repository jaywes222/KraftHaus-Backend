using Commerce.Application.Abstractions;
using Commerce.Application.Menus;
using Common.Application.Commerce;
using Common.Application.EventCatalog;

namespace Commerce.Application.Boards;

/// <summary><see cref="ICommerceBoards" /> for display screens: the public menu and the ready orders of an event.</summary>
public sealed class CommerceBoards(
    MenuService menu,
    IKraftOrderRepository orders,
    IKraftRepository krafts,
    IEventCatalog events) : ICommerceBoards
{
    public async Task<IReadOnlyList<MenuBoardKraft>> GetMenuAsync(
        Guid eventId,
        IReadOnlyCollection<Guid> kraftIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kraftIds);

        var sale = await events.GetSaleSnapshotAsync(eventId, cancellationToken);
        if (sale is null)
        {
            return [];
        }

        var publicMenu = await menu.PublicMenuAsync(sale.Slug, cancellationToken);
        if (publicMenu.IsFailure || publicMenu.Value is not { } view)
        {
            return [];
        }

        return
        [
            .. view.Krafts
                .Where(x => kraftIds.Count == 0 || kraftIds.Contains(x.KraftId))
                .Select(x => new MenuBoardKraft(
                    x.KraftId,
                    x.Name,
                    x.Stall,
                    [.. x.Items.Where(i => i.SoldLive && i.Category != nameof(Commerce.Domain.Menus.MenuCategory.Activity)).Select(i => new MenuBoardItem(i.Id, i.Name, i.Category, i.Price, i.AgeRestricted, i.SoldOut))]))
        ];
    }

    public async Task<IReadOnlyList<ReadyOrder>> GetReadyOrdersAsync(
        Guid eventId,
        IReadOnlyCollection<Guid> kraftIds,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kraftIds);

        var ready = await orders.ListReadyForEventAsync(eventId, kraftIds, Math.Clamp(limit, 1, 200), cancellationToken);
        if (ready.Count == 0)
        {
            return [];
        }

        var stalls = (await krafts.ListByIdsForReadAsync([.. ready.Select(x => x.KraftId).Distinct()], cancellationToken))
            .ToDictionary(x => x.Id.Value, x => x.AtEvent(eventId)?.Stall);

        return
        [
            .. ready.Select(x => new ReadyOrder(
                x.Reference,
                x.KraftId,
                x.KraftName,
                stalls.GetValueOrDefault(x.KraftId),
                x.ReadyAt ?? x.PaidAt ?? DateTimeOffset.MinValue))
        ];
    }
}
