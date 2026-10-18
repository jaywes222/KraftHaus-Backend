using Commerce.Application.Abstractions;
using Commerce.Domain.Common;
using Commerce.Domain.Orders;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;

namespace Commerce.Application.Orders;

/// <summary>Finance and Kraft Owner view of food and drink sales, with the fee split (Spec 08 §7.4), and the refund queue.</summary>
public sealed class KraftOrderAdminService(
    IKraftOrderRepository orders,
    ICommerceUnitOfWork unitOfWork,
    TimeProvider time)
{
    private const int MaxLimit = 200;

    public async Task<Result<IReadOnlyList<KraftOrderAdminView>>> ListAsync(
        Guid? eventId,
        Guid? kraftId,
        KraftOrderStatus? status,
        bool? needsAction,
        DateTimeOffset? before,
        int? limit,
        CancellationToken cancellationToken)
    {
        var list = await orders.ListForAdminAsync(eventId, kraftId, status, needsAction ?? false, before, Math.Clamp(limit ?? 50, 1, MaxLimit), cancellationToken);

        return Result<IReadOnlyList<KraftOrderAdminView>>.Success([.. list.Select(KraftOrderAdminView.From)]);
    }

    public async Task<Result<KraftOrderAdminView>> GetAsync(KraftOrderId id, CancellationToken cancellationToken)
    {
        var order = await orders.FindForReadAsync(id, cancellationToken);

        return order is null ? CommerceErrors.NotFound("OrderNotFound", "The order was not found.") : KraftOrderAdminView.From(order);
    }

    /// <summary>Finance refunded the money by hand (D-01) and closes the order's open issue with a note.</summary>
    public async Task<Result<KraftOrderAdminView>> ResolveAsync(
        KraftOrderId id,
        ResolveKraftOrderRequest request,
        ApplicationUserId resolvedBy,
        CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(id, cancellationToken);
        if (order is null)
        {
            return CommerceErrors.NotFound("OrderNotFound", "The order was not found.");
        }

        var resolved = order.ResolveAttention(request?.Resolution, resolvedBy.Value, time.GetUtcNow());
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);

        return saved.IsFailure ? saved.Error! : KraftOrderAdminView.From(order);
    }

    /// <summary>Paid sales per Kraft at an event (all Krafts, or one).</summary>
    public async Task<Result<IReadOnlyList<KraftSalesView>>> SalesAsync(Guid eventId, Guid? kraftId, CancellationToken cancellationToken)
    {
        var paid = await orders.ListPaidAsync(eventId, kraftId, cancellationToken);

        return Result<IReadOnlyList<KraftSalesView>>.Success(
        [
            .. paid.GroupBy(x => x.KraftId)
                .Select(x => new KraftSalesView(
                    x.Key,
                    x.First().KraftName,
                    x.Count(),
                    x.Sum(o => o.Lines.Sum(l => l.Quantity)),
                    x.Sum(o => o.Total),
                    x.Sum(o => o.PlatformFee ?? 0m),
                    x.Sum(o => o.KraftRevenue ?? 0m),
                    x.Sum(o => o.AmountToRefund),
                    x.Sum(o => o.ServiceFee)))
                .OrderBy(x => x.KraftName, StringComparer.OrdinalIgnoreCase)
        ]);
    }
}
