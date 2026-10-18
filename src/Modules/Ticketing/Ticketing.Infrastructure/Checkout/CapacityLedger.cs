using System.Net;
using Common.Domain.ResultMonad;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Checkout;
using Ticketing.Domain.Common;
using Ticketing.Infrastructure.Persistence;

namespace Ticketing.Infrastructure.Checkout;

/// <summary>
///     Spec 08 §8.1 in SQL. Every change is one conditional <c>UPDATE</c>; "rows affected = 0" means the bucket is full.
///     Runs on the Ticketing context's connection, so it joins the caller's transaction.
/// </summary>
internal sealed class CapacityLedger(TicketingDbContext db, TimeProvider time) : ICapacityLedger
{
    private const string Held = "Held";
    private const string Converted = "Converted";
    private const string Released = "Released";
    private const string Returned = "Returned";

    public async Task<Result> HoldAsync(
        OrderId orderId,
        IReadOnlyList<CapacityClaim> claims,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var full = new List<string>();

        // Stable order (kind, ref) so two concurrent orders lock buckets in the same sequence: no deadlocks.
        foreach (var claim in claims.OrderBy(x => x.Kind).ThenBy(x => x.RefId))
        {
            var kind = claim.Kind.ToString();
            var now = time.GetUtcNow();

            // Keep the bucket's size in step with Events (an admin may have changed the capacity).
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO ticketing.capacity_bucket ("Id", "Kind", "RefId", "EventId", "Total", "Held", "Sold")
                 VALUES ({Guid.CreateVersion7(now)}, {kind}, {claim.RefId}, {claim.EventId}, {claim.Total}, 0, 0)
                 ON CONFLICT ("Kind", "RefId") DO UPDATE SET "Total" = EXCLUDED."Total"
                 """,
                cancellationToken);

            var taken = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE ticketing.capacity_bucket
                 SET "Held" = "Held" + {claim.Quantity}
                 WHERE "Kind" = {kind} AND "RefId" = {claim.RefId}
                   AND "Sold" + "Held" + {claim.Quantity} <= "Total"
                 """,
                cancellationToken);

            if (taken == 0)
            {
                full.Add($"{claim.Label}: Not enough left.");
                continue;
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO ticketing.capacity_hold ("Id", "OrderId", "Kind", "RefId", "Quantity", "ExpiresAt", "Status")
                 VALUES ({Guid.CreateVersion7(now)}, {orderId.Value}, {kind}, {claim.RefId}, {claim.Quantity}, {expiresAt}, {Held})
                 """,
                cancellationToken);
        }

        return full.Count == 0
            ? Result.Success
            : new Error
            {
                Key = "Ticketing.SoldOut",
                Description = "Some of the tickets you chose are sold out or have fewer places left.",
                StatusCode = HttpStatusCode.Conflict,
                SubErrors = full
            };
    }

    public async Task<bool> ConvertAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE ticketing.capacity_bucket b
             SET "Held" = b."Held" - h."Quantity", "Sold" = b."Sold" + h."Quantity"
             FROM ticketing.capacity_hold h
             WHERE h."OrderId" = {orderId.Value} AND h."Status" = {Held}
               AND b."Kind" = h."Kind" AND b."RefId" = h."RefId"
             """,
            cancellationToken);

        var converted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE ticketing.capacity_hold SET "Status" = {Converted} WHERE "OrderId" = {orderId.Value} AND "Status" = {Held}""",
            cancellationToken);

        return converted > 0;
    }

    public async Task ReleaseAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE ticketing.capacity_bucket b
             SET "Held" = b."Held" - h."Quantity"
             FROM ticketing.capacity_hold h
             WHERE h."OrderId" = {orderId.Value} AND h."Status" = {Held}
               AND b."Kind" = h."Kind" AND b."RefId" = h."RefId"
             """,
            cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE ticketing.capacity_hold SET "Status" = {Released} WHERE "OrderId" = {orderId.Value} AND "Status" = {Held}""",
            cancellationToken);
    }

    public async Task<Result> ReacquireAsSoldAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        var released = await db.Database
            .SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM ticketing.capacity_hold WHERE "OrderId" = {orderId.Value} AND "Status" = {Released}""")
            .SingleAsync(cancellationToken);

        var taken = released == 0
            ? 0
            : await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE ticketing.capacity_bucket b
                 SET "Sold" = b."Sold" + h."Quantity"
                 FROM ticketing.capacity_hold h
                 WHERE h."OrderId" = {orderId.Value} AND h."Status" = {Released}
                   AND b."Kind" = h."Kind" AND b."RefId" = h."RefId"
                   AND b."Sold" + b."Held" + h."Quantity" <= b."Total"
                 """,
                cancellationToken);

        if (released == 0 || taken < released)
        {
            return new Error
            {
                Key = "Ticketing.SoldOut",
                Description = "The seats of this order were sold to someone else after its hold ran out.",
                StatusCode = HttpStatusCode.Conflict
            };
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE ticketing.capacity_hold SET "Status" = {Converted} WHERE "OrderId" = {orderId.Value} AND "Status" = {Released}""",
            cancellationToken);

        return Result.Success;
    }

    public async Task ReturnSoldAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE ticketing.capacity_bucket b
             SET "Sold" = b."Sold" - h."Quantity"
             FROM ticketing.capacity_hold h
             WHERE h."OrderId" = {orderId.Value} AND h."Status" = {Converted}
               AND b."Kind" = h."Kind" AND b."RefId" = h."RefId"
             """,
            cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE ticketing.capacity_hold SET "Status" = {Returned} WHERE "OrderId" = {orderId.Value} AND "Status" = {Converted}""",
            cancellationToken);
    }

    public async Task<IReadOnlyList<CapacityBucketView>> ListForEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var rows = await db.Set<CapacityBucketRow>()
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.Kind).ThenBy(x => x.RefId)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => new CapacityBucketView(Enum.Parse<CapacityBucketKind>(x.Kind), x.RefId, x.Total, x.Held, x.Sold))];
    }
}
