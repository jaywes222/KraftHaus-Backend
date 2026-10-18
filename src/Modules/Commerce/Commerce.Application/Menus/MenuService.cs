using Commerce.Application.Abstractions;
using Commerce.Application.Boards;
using Commerce.Application.Images;
using Commerce.Application.Krafts;
using Commerce.Application.Stock;
using Commerce.Domain.Common;
using Commerce.Domain.Krafts;
using Commerce.Domain.Menus;
using Common.Application.EventCatalog;
using Common.Application.Payments;
using Common.Domain.ResultMonad;
using Microsoft.Extensions.Options;

namespace Commerce.Application.Menus;

/// <summary>
///     Menus (UF-017): the Kraft Owner (or Tenant Admin) prices items and sets how many portions go to live orders and to
///     reservations; kitchen staff switch items off when they run out (UF-015). Every call names the Kraft in its route,
///     and an item of another Kraft answers 404 (BR-026).
/// </summary>
public sealed class MenuService(
    IMenuItemRepository items,
    IKraftRepository krafts,
    IStockLedger stock,
    ICommerceUnitOfWork unitOfWork,
    IEventCatalog events,
    KraftTills tills,
    IOptions<CommerceOptions> options,
    DisplaySignals displays,
    TimeProvider time)
{
    public async Task<Result<MenuItemAdminView>> AddAsync(KraftId kraftId, AddMenuItemRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kraft = await krafts.FindForReadAsync(kraftId, cancellationToken);
        if (kraft is null)
        {
            return CommerceErrors.KraftNotFound;
        }

        if (kraft.AtEvent(request.EventId) is null)
        {
            return CommerceErrors.NotFound("KraftNotAtEvent", "This Kraft does not take part in that event.");
        }

        var created = MenuItem.Create(new MenuItemId(Guid.CreateVersion7()), kraftId.Value, request.EventId, request.ToSpec());
        if (created.IsFailure)
        {
            return created.Error!;
        }

        items.Add(created.Value!);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        await displays.MenuChangedAsync(created.Value!.EventId, cancellationToken);

        return MenuItemAdminView.From(created.Value, []);
    }

    public async Task<Result<IReadOnlyList<MenuItemAdminView>>> ListAsync(
        KraftId kraftId,
        Guid? eventId,
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        var list = await items.ListForKraftAsync(kraftId.Value, eventId, includeRetired, cancellationToken);
        var levels = await stock.ListAsync([.. list.Select(x => x.Id.Value)], cancellationToken);

        return Result<IReadOnlyList<MenuItemAdminView>>.Success([.. list.Select(x => MenuItemAdminView.From(x, levels))]);
    }

    public Task<Result<MenuItemAdminView>> UpdateAsync(KraftId kraftId, MenuItemId itemId, MenuItemRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ChangeAsync(kraftId, itemId, item => item.Update(request.ToSpec()), cancellationToken);
    }

    public Task<Result<MenuItemAdminView>> RetireAsync(KraftId kraftId, MenuItemId itemId, CancellationToken cancellationToken) =>
        ChangeAsync(kraftId, itemId, item => item.Retire(), cancellationToken);

    /// <summary>Kitchen switch (UF-015): off stops new orders; paid orders keep their portions.</summary>
    public Task<Result<MenuItemAdminView>> SetAvailableAsync(KraftId kraftId, MenuItemId itemId, SetAvailabilityRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(kraftId, itemId, item => item.SetAvailable(request?.Available ?? false), cancellationToken);

    /// <summary>The public menu of an event: Krafts with an approved till and their active items, with what is left.</summary>
    public async Task<Result<PublicMenuView>> PublicMenuAsync(string slug, CancellationToken cancellationToken)
    {
        var snapshot = await events.FindBySlugAsync(slug, cancellationToken);
        if (snapshot is null || snapshot.Status == EventLifecycle.Draft)
        {
            return CommerceErrors.EventNotFound;
        }

        var now = time.GetUtcNow();
        var windows = OrderingWindows.For(snapshot, options.Value);
        var active = await items.ListActiveForEventAsync(snapshot.EventId, cancellationToken);
        var candidates = (await krafts.ListByIdsForReadAsync([.. active.Select(x => x.KraftId).Distinct()], cancellationToken))
            .Where(x => x.Status == KraftStatus.Active && x.AtEvent(snapshot.EventId) is not null)
            .ToList();
        var selling = await tills.SellingAsync(snapshot.EventId, candidates, cancellationToken);
        var menu = active.Where(x => selling.Contains(x.KraftId)).ToList();
        var levels = await stock.ListAsync([.. menu.Select(x => x.Id.Value)], cancellationToken);
        var sellers = candidates
            .Where(x => selling.Contains(x.Id.Value))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase);

        return new PublicMenuView(
            snapshot.EventId,
            snapshot.Slug,
            windows.ReservationsOpenAt(now, snapshot.Status),
            windows.ReservationsCloseAt,
            windows.LiveOpenAt(now, snapshot.Status),
            windows.LiveOpensAt,
            windows.LiveClosesAt,
            [
                .. sellers.Select(kraft => new PublicMenuKraftView(
                    kraft.Id.Value,
                    kraft.Name,
                    kraft.Slug,
                    kraft.AtEvent(snapshot.EventId)?.Stall,
                    [
                        .. menu.Where(x => x.KraftId == kraft.Id.Value)
                            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                            .Select(x => PublicItem(x, levels))
                    ],
                    CommerceImageUrls.For(kraft.LogoPath)))
            ]);
    }

    private static PublicMenuItemView PublicItem(MenuItem item, IReadOnlyList<StockLevel> levels)
    {
        var view = MenuItemAdminView.From(item, levels);

        return new PublicMenuItemView(
            item.Id.Value,
            item.Name,
            item.Description,
            item.Category.ToString(),
            item.Price,
            item.AgeRestricted,
            item.SoldLive,
            !item.Available || !item.SoldLive || view.LiveLeft == 0,
            view.LiveLeft,
            item.Reservable,
            view.ReservationsLeft,
            item.AddOns,
            item.StartsAt,
            item.DurationMinutes,
            view.ImageHref);
    }

    private async Task<Result<MenuItemAdminView>> ChangeAsync(
        KraftId kraftId,
        MenuItemId itemId,
        Func<MenuItem, Result> change,
        CancellationToken cancellationToken)
    {
        var item = await items.FindAsync(itemId, cancellationToken);
        if (item is null || item.KraftId != kraftId.Value)
        {
            return CommerceErrors.NotFound("MenuItemNotFound", "The menu item was not found.");
        }

        var changed = change(item);
        if (changed.IsFailure)
        {
            return changed.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        await displays.MenuChangedAsync(item.EventId, cancellationToken);

        var levels = await stock.ListAsync([item.Id.Value], cancellationToken);

        return MenuItemAdminView.From(item, levels);
    }
}
