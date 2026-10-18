using Commerce.Application.Abstractions;
using Commerce.Domain.Common;
using Commerce.Domain.Orders;
using Common.Domain.Codes;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;

namespace Commerce.Application.Orders;

/// <summary>
///     The kitchen board and pickup (UF-014, UF-015): a Kraft's paid orders only, moved New → Preparing → Ready →
///     Collected; handing over by scanning the receipt QR (or typing its code); a line that ran out after payment is
///     marked unavailable (O-05). Every call names the Kraft; another Kraft's order answers 404 (BR-026).
/// </summary>
public sealed class KitchenService(
    IKraftOrderRepository orders,
    IMenuItemRepository menu,
    ICommerceUnitOfWork unitOfWork,
    IKraftOrderNotifier notifier,
    IKraftOrderRealtime realtime,
    TimeProvider time)
{
    /// <summary>Collected orders stay on the board this long so staff can see what was just handed over.</summary>
    private static readonly TimeSpan CollectedVisibleFor = TimeSpan.FromMinutes(60);

    public async Task<Result<IReadOnlyList<KitchenOrderView>>> BoardAsync(KraftId kraftId, Guid? eventId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var board = await orders.ListBoardAsync(kraftId.Value, eventId, now - CollectedVisibleFor, cancellationToken);

        return Result<IReadOnlyList<KitchenOrderView>>.Success([.. board.Select(x => KitchenOrderView.From(x, now))]);
    }

    public Task<Result<KitchenOrderView>> AcceptAsync(KraftId kraftId, KraftOrderId orderId, CancellationToken cancellationToken) =>
        ChangeAsync(kraftId, orderId, order => order.Accept(time.GetUtcNow()), cancellationToken);

    public async Task<Result<KitchenOrderView>> ReadyAsync(KraftId kraftId, KraftOrderId orderId, CancellationToken cancellationToken)
    {
        var changed = await ChangeAsync(kraftId, orderId, order => order.MarkReady(time.GetUtcNow()), cancellationToken);

        if (!changed.IsFailure && await orders.FindForReadAsync(orderId, cancellationToken) is { } ready)
        {
            await notifier.OrderReadyAsync(ready, CancellationToken.None);
        }

        return changed;
    }

    public Task<Result<KitchenOrderView>> CollectAsync(KraftId kraftId, KraftOrderId orderId, ApplicationUserId staff, CancellationToken cancellationToken) =>
        ChangeAsync(kraftId, orderId, order => order.Collect(staff.Value, time.GetUtcNow()), cancellationToken);

    /// <summary>Hands an order over by its receipt QR / pickup code. Codes of other Krafts' orders are not found here.</summary>
    public async Task<Result<KitchenOrderView>> CollectByCodeAsync(
        KraftId kraftId,
        CollectByCodeRequest request,
        ApplicationUserId staff,
        CancellationToken cancellationToken)
    {
        var code = ShortCode.Normalize(request?.Code);
        if (code.Length != KraftOrder.PickupCodeLength)
        {
            return CommerceErrors.Invalid("PickupCodeInvalid", ["Code: Scan the receipt QR or type its 8-character code."]);
        }

        var order = await orders.FindByPickupCodeAsync(kraftId.Value, code, cancellationToken);

        return order is null
            ? OrderNotFound
            : await ChangeAsync(kraftId, order.Id, current => current.Collect(staff.Value, time.GetUtcNow()), cancellationToken);
    }

    public async Task<Result<KitchenOrderView>> LineUnavailableAsync(
        KraftId kraftId,
        KraftOrderId orderId,
        KraftOrderLineId lineId,
        LineUnavailableRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var order = await orders.FindAsync(orderId, cancellationToken);
        if (order is null || order.KraftId != kraftId.Value)
        {
            return OrderNotFound;
        }

        var marked = order.MarkLineUnavailable(lineId, request.Note, time.GetUtcNow());
        if (marked.IsFailure)
        {
            return marked.Error!;
        }

        if (request.SwitchOffItem &&
            order.Lines.First(x => x.Id == lineId).MenuItemId is var itemId &&
            await menu.FindAsync(new MenuItemId(itemId), cancellationToken) is { } item)
        {
            item.SetAvailable(false);
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        await realtime.OrderChangedAsync(order, CancellationToken.None);

        return KitchenOrderView.From(order, time.GetUtcNow());
    }

    private static Error OrderNotFound => CommerceErrors.NotFound("OrderNotFound", "No order of this Kraft matches.");

    private async Task<Result<KitchenOrderView>> ChangeAsync(
        KraftId kraftId,
        KraftOrderId orderId,
        Func<KraftOrder, Result> change,
        CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(orderId, cancellationToken);
        if (order is null || order.KraftId != kraftId.Value)
        {
            return OrderNotFound;
        }

        var changed = change(order);
        if (changed.IsFailure)
        {
            return changed.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        await realtime.OrderChangedAsync(order, CancellationToken.None);

        return KitchenOrderView.From(order, time.GetUtcNow());
    }
}
