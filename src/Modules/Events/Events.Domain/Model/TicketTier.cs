using Common.Domain.Entities;
using Events.Domain.Common;

namespace Events.Domain.Model;

/// <summary>
///     A ticket category on one event date ("Regular", "VIP") with its own capacity, priced through
///     <see cref="Phases" /> and optionally sold as <see cref="Offers" /> (Couple / Gang bundles).
/// </summary>
public sealed class TicketTier : AuditableEntity<TicketTierId>
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 500;
    public const int MaxCapacity = 1_000_000;

    private readonly List<SalePhase> _phases = [];
    private readonly List<GroupOffer> _offers = [];

    private TicketTier(TicketTierId id)
        : base(id)
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Admissions available on this tier for this date (counted per attendee, §8.1).</summary>
    public int Capacity { get; private set; }

    /// <summary>Display order within the date.</summary>
    public int Position { get; private set; }

    public SaleItemStatus Status { get; private set; }

    public IReadOnlyList<SalePhase> Phases => _phases.AsReadOnly();

    public IReadOnlyList<GroupOffer> Offers => _offers.AsReadOnly();

    /// <summary>The phase whose window contains <paramref name="now" />, if any.</summary>
    public SalePhase? CurrentPhase(DateTimeOffset now) => _phases.Find(x => x.Window.Contains(now));

    /// <summary>The next phase to start after <paramref name="now" />, if any.</summary>
    public SalePhase? NextPhase(DateTimeOffset now) =>
        _phases.Where(x => x.StartsAt > now).OrderBy(x => x.StartsAt).FirstOrDefault();

    internal bool HasPricing =>
        _phases.Count > 0 || _offers.Exists(x => x.Status == SaleItemStatus.Active);

    internal static TicketTier Create(
        TicketTierId id,
        string name,
        string? description,
        int capacity,
        int position) =>
        new(id)
        {
            Name = name,
            Description = description,
            Capacity = capacity,
            Position = position,
            Status = SaleItemStatus.Active
        };

    internal void Update(string name, string? description, int capacity)
    {
        Name = name;
        Description = description;
        Capacity = capacity;
    }

    internal void Withdraw() => Status = SaleItemStatus.Withdrawn;

    internal SalePhase? FindPhase(SalePhaseId phaseId) => _phases.Find(x => x.Id == phaseId);

    internal GroupOffer? FindOffer(GroupOfferId offerId) => _offers.Find(x => x.Id == offerId);

    internal bool PhaseOverlaps(SalesWindow window, SalePhaseId? except) =>
        _phases.Exists(x => x.Id != except && x.Window.Overlaps(window));

    internal void AddPhase(SalePhase phase) => _phases.Add(phase);

    internal void RemovePhase(SalePhase phase) => _phases.Remove(phase);

    internal void AddOffer(GroupOffer offer) => _offers.Add(offer);

    internal void RemoveOffer(GroupOffer offer) => _offers.Remove(offer);
}
