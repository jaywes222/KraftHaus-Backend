using System.Globalization;
using Common.Application.Operations;
using Microsoft.Extensions.Options;
using Common.Application.Analytics;
using Common.Application.Communications;
using Common.Application.Ticketing;
using Common.Domain.Communications;
using Common.Domain.TimeProvision;
using Microsoft.EntityFrameworkCore;
using Ticketing.Domain.Orders;
using Ticketing.Domain.Tickets;
using Ticketing.Infrastructure.Persistence;

namespace Ticketing.Infrastructure.ReadModels;

/// <summary>Broadcast audiences for Communications (UF-020): phones only, de-duplicated, consent applied for marketing.</summary>
internal sealed class AudienceDirectory(TicketingDbContext db) : IAudienceDirectory
{
    public async Task<IReadOnlyList<AudienceMember>> ListAsync(Guid eventId, BroadcastAudience audience, CancellationToken cancellationToken)
    {
        switch (audience)
        {
            case BroadcastAudience.WaitlistMarketing:
                return Members(await WaitlistAsync(eventId, cancellationToken));

            case BroadcastAudience.WaitlistWithoutTicket:
                var holders = (await HolderPhones(eventId).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
                return Members((await WaitlistAsync(eventId, cancellationToken)).Where(x => !holders.Contains(x.Phone)));

            case BroadcastAudience.TicketHolders:
                var tickets = await db.Tickets.AsNoTracking()
                    .Where(x => x.EventId == eventId && x.Status != TicketStatus.Void)
                    .Select(x => new { Phone = x.HolderPhone, Name = x.HolderName })
                    .ToListAsync(cancellationToken);
                return Members(tickets.Select(x => (x.Phone, x.Name)));

            default:
                return [];
        }
    }

    private async Task<List<(string Phone, string Name)>> WaitlistAsync(Guid eventId, CancellationToken cancellationToken) =>
        [.. (await db.WaitlistEntries.AsNoTracking()
                .Where(x => x.EventId == eventId && x.MarketingConsent)
                .Select(x => new { x.Phone, x.Name })
                .ToListAsync(cancellationToken))
            .Select(x => (x.Phone, x.Name))];

    private IQueryable<string> HolderPhones(Guid eventId) =>
        db.Tickets.AsNoTracking()
            .Where(x => x.EventId == eventId && x.Status != TicketStatus.Void)
            .Select(x => x.HolderPhone)
            .Distinct();

    private static List<AudienceMember> Members(IEnumerable<(string Phone, string Name)> rows) =>
        [.. rows
            .GroupBy(x => x.Phone, StringComparer.Ordinal)
            .Select(x => new AudienceMember(x.Key, FirstName(x.First().Name)))];

    internal static string? FirstName(string? name)
    {
        var first = name?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrEmpty(first) ? null : first;
    }
}

/// <summary>Checked-in tickets of a phone at an event, for Feedback (UF-010).</summary>
internal sealed class Attendance(TicketingDbContext db) : IAttendance
{
    public async Task<IReadOnlyList<AttendedDate>> ListAttendedAsync(string phone, Guid eventId, CancellationToken cancellationToken)
    {
        var rows = await db.Tickets.AsNoTracking()
            .Where(x => x.HolderPhone == phone && x.EventId == eventId && x.Status == TicketStatus.CheckedIn && x.CheckedInAt != null)
            .Select(x => new { Id = x.Id, x.EventDateId, x.DateStartsAt, x.CheckedInAt })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => new AttendedDate(x.Id.Value, x.EventDateId, x.DateStartsAt, x.CheckedInAt!.Value))];
    }
}

/// <summary>Ticketing figures for the Analytics dashboard (UF-023). Counts and amounts only.</summary>
internal sealed class TicketingInsightsProvider(TicketingDbContext db) : ITicketingInsights
{
    /// <summary>Check-ins are counted in quarter-hour slots: the door rush is what operators watch.</summary>
    public const int CheckInSlotMinutes = 15;

    public async Task<TicketingInsights> GetAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var waitlist = await db.WaitlistEntries.AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => new { x.JoinedAt, x.MarketingConsent })
            .ToListAsync(cancellationToken);

        var orders = await db.Orders.AsNoTracking()
            .Where(x => x.EventId == eventId && (x.Status == OrderStatus.Paid || x.Status == OrderStatus.PartiallyPaid))
            .Select(x => new { FirstMoneyAt = x.DepositPaidAt ?? x.PaidAt ?? x.PlacedAt, x.AmountPaid, x.PlatformFee })
            .ToListAsync(cancellationToken);

        var tickets = await db.Tickets.AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => new { x.TierId, x.TierName, x.Status, x.EventDateId, x.CheckedInAt })
            .ToListAsync(cancellationToken);

        int Count(TicketStatus status) => tickets.Count(x => x.Status == status);

        return new TicketingInsights(
            waitlist.Count,
            waitlist.Count(x => x.MarketingConsent),
            [.. waitlist.GroupBy(x => Day(x.JoinedAt)).OrderBy(x => x.Key).Select(x => new DayCount(x.Key, x.Count()))],
            [.. orders.GroupBy(x => Day(x.FirstMoneyAt)).OrderBy(x => x.Key).Select(x => new DayAmount(x.Key, x.Count(), x.Sum(o => o.AmountPaid)))],
            [.. tickets
                .Where(x => x.Status != TicketStatus.Void)
                .GroupBy(x => x.TierId)
                .Select(x => new TierCount(x.Key, x.First().TierName, x.Count(), x.Count(t => t.Status == TicketStatus.CheckedIn)))
                .OrderByDescending(x => x.Sold)],
            Count(TicketStatus.Valid),
            Count(TicketStatus.CheckedIn),
            Count(TicketStatus.AwaitingBalance),
            Count(TicketStatus.Void),
            orders.Count,
            orders.Sum(x => x.AmountPaid),
            orders.Sum(x => x.PlatformFee),
            [.. tickets
                .Where(x => x.Status == TicketStatus.CheckedIn && x.CheckedInAt != null)
                .GroupBy(x => (x.EventDateId, Slot: Slot(x.CheckedInAt!.Value, CheckInSlotMinutes)))
                .OrderBy(x => x.Key.Slot)
                .Select(x => new SlotCount(x.Key.EventDateId, x.Key.Slot, x.Count()))]);
    }

    internal static DateOnly Day(DateTimeOffset instant) => instant.ToNairobiDate();

    internal static DateTimeOffset Slot(DateTimeOffset instant, int minutes)
    {
        var utc = instant.ToUniversalTime();
        var ticks = TimeSpan.FromMinutes(minutes).Ticks;
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % ticks), TimeSpan.Zero);
    }
}

/// <summary>Operator alert (Spec 08 §14 "hold-expiry backlog"): orders whose hold ran out long ago but were never released.</summary>
internal sealed class HoldBacklogSignal(TicketingDbContext db, IOptions<OperationalAlertOptions> options, TimeProvider time) : IOperationalSignal
{
    public async Task<IReadOnlyList<OperationalIssue>> CheckAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow().AddMinutes(-options.Value.BacklogMinutes);
        var overdue = await db.Orders.AsNoTracking()
            .CountAsync(x => x.Status == OrderStatus.AwaitingPayment && x.HoldExpiresAt < cutoff, cancellationToken);

        return overdue == 0
            ? []
            : [new OperationalIssue("ticketing.holds", string.Create(CultureInfo.InvariantCulture,
                $"{overdue} ticket hold(s) expired over {options.Value.BacklogMinutes} min ago and were not released (hold job not running?)."))];
    }
}
