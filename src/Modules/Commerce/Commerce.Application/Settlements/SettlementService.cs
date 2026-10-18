using Commerce.Application.Abstractions;
using Commerce.Domain.Common;
using Commerce.Domain.Krafts;
using Commerce.Domain.Orders;
using Commerce.Domain.Settlements;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;

namespace Commerce.Application.Settlements;

public interface IKraftSettlementRepository
{
    Task<IReadOnlyList<KraftSettlement>> ListForEventAsync(Guid eventId, Guid? kraftId, CancellationToken cancellationToken);

    void Add(KraftSettlement settlement);
}

public sealed record SettlementView(
    Guid Id,
    Guid KraftId,
    Guid EventId,
    string Direction,
    decimal Amount,
    string Reference,
    string? Note,
    DateTimeOffset PaidAt)
{
    public static SettlementView From(KraftSettlement x)
    {
        ArgumentNullException.ThrowIfNull(x);
        return new SettlementView(x.Id.Value, x.KraftId, x.EventId, x.Direction.ToString(), x.Amount, x.Reference, x.Note, x.PaidAt);
    }
}

/// <summary>
///     What a Kraft and the platform owe each other for one event. Positive <see cref="Balance" />: the platform still
///     owes the Kraft; negative: the Kraft still owes the platform.
/// </summary>
/// <param name="CollectedInOwnTill">Money paid into the Kraft's own till (the Kraft holds it).</param>
/// <param name="CollectedInPlatformTill">Money paid into the KRAFTHAUS till (the platform holds it).</param>
/// <param name="Commission">Platform commission on the food and drinks subtotal.</param>
/// <param name="ServiceFees">KES 30 service fees paid by customers.</param>
/// <param name="KraftShare">What the Kraft keeps (subtotal minus commission).</param>
/// <param name="PlatformOwesKraft">The Kraft's share of money in the platform's till.</param>
/// <param name="KraftOwesPlatform">The platform's share (commission and fees) of money in the Kraft's till.</param>
/// <param name="RefundsOwed">Owed back to customers (unavailable lines, late money); handled by Finance separately.</param>
public sealed record KraftStatementView(
    Guid KraftId,
    string KraftName,
    string PaymentRouting,
    decimal CommissionPercent,
    int PaidOrders,
    decimal Gross,
    decimal CollectedInOwnTill,
    decimal CollectedInPlatformTill,
    decimal Commission,
    decimal ServiceFees,
    decimal KraftShare,
    decimal PlatformOwesKraft,
    decimal KraftOwesPlatform,
    decimal PaidToKraft,
    decimal PaidToPlatform,
    decimal Balance,
    decimal RefundsOwed,
    IReadOnlyList<SettlementView> Settlements);

public sealed record RecordSettlementRequest(
    Guid EventId,
    SettlementDirection Direction,
    decimal Amount,
    string? Reference,
    string? Note,
    DateTimeOffset? PaidAt);

/// <summary>Kraft statements for an event and the settlement payments recorded against them (Finance).</summary>
public sealed class SettlementService(
    IKraftOrderRepository orders,
    IKraftRepository krafts,
    IKraftSettlementRepository settlements,
    ICommerceUnitOfWork unitOfWork,
    TimeProvider time)
{
    public async Task<Result<IReadOnlyList<KraftStatementView>>> StatementsAsync(Guid eventId, Guid? kraftId, CancellationToken cancellationToken)
    {
        var paid = await orders.ListPaidAsync(eventId, kraftId, cancellationToken);
        var recorded = await settlements.ListForEventAsync(eventId, kraftId, cancellationToken);
        IReadOnlyList<Kraft> atEvent;
        if (kraftId is { } one)
        {
            var single = await krafts.FindForReadAsync(new KraftId(one), cancellationToken);
            atEvent = single is null ? [] : [single];
        }
        else
        {
            atEvent = await krafts.ListForReadAsync(null, eventId, cancellationToken);
        }

        var ids = atEvent.Select(x => x.Id.Value)
            .Concat(paid.Select(x => x.KraftId))
            .Concat(recorded.Select(x => x.KraftId))
            .Distinct()
            .ToList();
        var byId = atEvent.ToDictionary(x => x.Id.Value);
        var missing = ids.Where(x => !byId.ContainsKey(x)).ToList();
        if (missing.Count > 0)
        {
            foreach (var extra in await krafts.ListByIdsForReadAsync(missing, cancellationToken))
            {
                byId[extra.Id.Value] = extra;
            }
        }

        return Result<IReadOnlyList<KraftStatementView>>.Success(
        [
            .. ids.Select(id => Statement(
                    id,
                    byId.GetValueOrDefault(id),
                    eventId,
                    [.. paid.Where(x => x.KraftId == id)],
                    [.. recorded.Where(x => x.KraftId == id)]))
                .OrderBy(x => x.KraftName, StringComparer.OrdinalIgnoreCase)
        ]);
    }

    public async Task<Result<KraftStatementView>> RecordAsync(
        KraftId kraftId,
        RecordSettlementRequest request,
        ApplicationUserId recordedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kraft = await krafts.FindForReadAsync(kraftId, cancellationToken);
        if (kraft is null)
        {
            return CommerceErrors.KraftNotFound;
        }

        var now = time.GetUtcNow();
        if (request.PaidAt is { } paidAt && paidAt > now.AddMinutes(5))
        {
            return CommerceErrors.Invalid("SettlementInvalid", ["PaidAt: A payment can't be in the future."]);
        }

        var recorded = KraftSettlement.Record(
            new KraftSettlementId(Guid.CreateVersion7(now)),
            kraftId.Value,
            request.EventId,
            request.Direction,
            request.Amount,
            request.Reference,
            request.Note,
            request.PaidAt ?? now,
            recordedBy.Value);
        if (recorded.IsFailure)
        {
            return recorded.Error!;
        }

        settlements.Add(recorded.Value!);
        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        var statements = await StatementsAsync(request.EventId, kraftId.Value, cancellationToken);
        return statements.Value!.Single();
    }

    /// <summary>Pure: the statement of one Kraft from its paid orders and recorded settlements.</summary>
    public static KraftStatementView Statement(
        Guid kraftId,
        Kraft? kraft,
        Guid eventId,
        IReadOnlyList<KraftOrder> paid,
        IReadOnlyList<KraftSettlement> recorded)
    {
        ArgumentNullException.ThrowIfNull(paid);
        ArgumentNullException.ThrowIfNull(recorded);

        var terms = kraft?.AtEvent(eventId);
        static decimal Sum(IEnumerable<KraftOrder> list, Func<KraftOrder, decimal> pick) => list.Sum(pick);

        var platformTill = paid.Where(x => x.PaidIntoPlatformTill).ToList();
        var ownTill = paid.Where(x => !x.PaidIntoPlatformTill).ToList();

        var serviceFees = Sum(paid, x => x.ServiceFee);
        var platformShare = Sum(paid, x => x.PlatformFee ?? 0m);
        var platformOwesKraft = Sum(platformTill, x => x.KraftRevenue ?? 0m);
        var kraftOwesPlatform = Sum(ownTill, x => x.PlatformFee ?? 0m);
        var paidToKraft = recorded.Where(x => x.Direction == SettlementDirection.PlatformPaidKraft).Sum(x => x.Amount);
        var paidToPlatform = recorded.Where(x => x.Direction == SettlementDirection.KraftPaidPlatform).Sum(x => x.Amount);

        return new KraftStatementView(
            kraftId,
            kraft?.Name ?? (paid.Count > 0 ? paid[0].KraftName : "Kraft"),
            (terms?.PaymentRouting ?? PaymentRouting.OwnTill).ToString(),
            terms?.CommissionPercent ?? 0m,
            paid.Count,
            Sum(paid, x => x.Total),
            Sum(ownTill, x => x.Total),
            Sum(platformTill, x => x.Total),
            platformShare - serviceFees,
            serviceFees,
            Sum(paid, x => x.KraftRevenue ?? 0m),
            platformOwesKraft,
            kraftOwesPlatform,
            paidToKraft,
            paidToPlatform,
            platformOwesKraft - kraftOwesPlatform - paidToKraft + paidToPlatform,
            Sum(paid, x => x.AmountToRefund),
            [.. recorded.OrderByDescending(x => x.PaidAt).Select(SettlementView.From)]);
    }
}
