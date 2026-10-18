using System.Net;
using Commerce.Application.Stock;
using Commerce.Infrastructure.Persistence;
using Common.Domain.ResultMonad;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Infrastructure.Stock;

/// <summary>
///     Stock in SQL, like ticket capacity (Spec 08 §8.1): every change is one conditional <c>UPDATE</c>; "rows affected =
///     0" means the bucket is empty. Runs on the Commerce context's connection, so it joins the caller's transaction.
/// </summary>
internal sealed class StockLedger(CommerceDbContext db, TimeProvider time) : IStockLedger
{
    private const string Held = "Held";
    private const string Converted = "Converted";
    private const string Released = "Released";

    public async Task<Result> HoldAsync(
        Guid orderId,
        IReadOnlyList<StockClaim> claims,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var empty = new List<string>();

        // Stable order (kind, item) so concurrent orders lock buckets in the same sequence: no deadlocks.
        foreach (var claim in claims.OrderBy(x => x.Kind).ThenBy(x => x.MenuItemId))
        {
            var kind = claim.Kind.ToString();
            var now = time.GetUtcNow();

            // Keep the bucket's size in step with the menu item (the Kraft may have changed its stock).
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO commerce.stock_bucket ("Id", "Kind", "MenuItemId", "EventId", "KraftId", "Total", "Held", "Sold")
                 VALUES ({Guid.CreateVersion7(now)}, {kind}, {claim.MenuItemId}, {claim.EventId}, {claim.KraftId}, {claim.Total}, 0, 0)
                 ON CONFLICT ("Kind", "MenuItemId") DO UPDATE SET "Total" = EXCLUDED."Total"
                 """,
                cancellationToken);

            var taken = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE commerce.stock_bucket
                 SET "Held" = "Held" + {claim.Quantity}
                 WHERE "Kind" = {kind} AND "MenuItemId" = {claim.MenuItemId}
                   AND ("Total" IS NULL OR "Sold" + "Held" + {claim.Quantity} <= "Total")
                 """,
                cancellationToken);

            if (taken == 0)
            {
                empty.Add($"{claim.Label}: Not enough left.");
                continue;
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO commerce.stock_hold ("Id", "OrderId", "Kind", "MenuItemId", "Quantity", "ExpiresAt", "Status")
                 VALUES ({Guid.CreateVersion7(now)}, {orderId}, {kind}, {claim.MenuItemId}, {claim.Quantity}, {expiresAt}, {Held})
                 """,
                cancellationToken);
        }

        return empty.Count == 0
            ? Result.Success
            : new Error
            {
                Key = "Commerce.SoldOut",
                Description = "Some of the items you chose are sold out or have fewer portions left.",
                StatusCode = HttpStatusCode.Conflict,
                SubErrors = empty
            };
    }

    public async Task<bool> ConvertAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE commerce.stock_bucket b
             SET "Held" = b."Held" - h."Quantity", "Sold" = b."Sold" + h."Quantity"
             FROM commerce.stock_hold h
             WHERE h."OrderId" = {orderId} AND h."Status" = {Held}
               AND b."Kind" = h."Kind" AND b."MenuItemId" = h."MenuItemId"
             """,
            cancellationToken);

        var converted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE commerce.stock_hold SET "Status" = {Converted} WHERE "OrderId" = {orderId} AND "Status" = {Held}""",
            cancellationToken);

        return converted > 0;
    }

    public async Task ReleaseAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE commerce.stock_bucket b
             SET "Held" = b."Held" - h."Quantity"
             FROM commerce.stock_hold h
             WHERE h."OrderId" = {orderId} AND h."Status" = {Held}
               AND b."Kind" = h."Kind" AND b."MenuItemId" = h."MenuItemId"
             """,
            cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE commerce.stock_hold SET "Status" = {Released} WHERE "OrderId" = {orderId} AND "Status" = {Held}""",
            cancellationToken);
    }

    public async Task<Result> ReacquireAsSoldAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var released = await db.Database
            .SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM commerce.stock_hold WHERE "OrderId" = {orderId} AND "Status" = {Released}""")
            .SingleAsync(cancellationToken);

        var taken = released == 0
            ? 0
            : await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE commerce.stock_bucket b
                 SET "Sold" = b."Sold" + h."Quantity"
                 FROM commerce.stock_hold h
                 WHERE h."OrderId" = {orderId} AND h."Status" = {Released}
                   AND b."Kind" = h."Kind" AND b."MenuItemId" = h."MenuItemId"
                   AND (b."Total" IS NULL OR b."Sold" + b."Held" + h."Quantity" <= b."Total")
                 """,
                cancellationToken);

        if (released == 0 || taken < released)
        {
            return new Error
            {
                Key = "Commerce.SoldOut",
                Description = "The portions of this order went to someone else after its hold ran out.",
                StatusCode = HttpStatusCode.Conflict
            };
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE commerce.stock_hold SET "Status" = {Converted} WHERE "OrderId" = {orderId} AND "Status" = {Released}""",
            cancellationToken);

        return Result.Success;
    }

    public async Task<IReadOnlyList<StockLevel>> ListAsync(IReadOnlyCollection<Guid> menuItemIds, CancellationToken cancellationToken)
    {
        if (menuItemIds.Count == 0)
        {
            return [];
        }

        var rows = await db.Set<StockBucketRow>()
            .AsNoTracking()
            .Where(x => menuItemIds.Contains(x.MenuItemId))
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => new StockLevel(Enum.Parse<StockKind>(x.Kind), x.MenuItemId, x.Total, x.Held, x.Sold))];
    }
}
