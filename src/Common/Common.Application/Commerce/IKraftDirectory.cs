namespace Common.Application.Commerce;

/// <summary>
///     Read-only view of Krafts for other modules (implemented by Commerce). Payments uses it to check that a till is
///     submitted for a Kraft that takes part in the event.
/// </summary>
public interface IKraftDirectory
{
    /// <summary>The Kraft if it exists, is active and takes part in the event; otherwise null.</summary>
    Task<KraftAtEvent?> FindAtEventAsync(Guid kraftId, Guid eventId, CancellationToken cancellationToken);

    /// <summary>Names of the given Krafts (missing ids are left out).</summary>
    Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> kraftIds, CancellationToken cancellationToken);
}

/// <param name="CommissionPercent">Platform commission on this Kraft's sales at the event (0-100).</param>
public sealed record KraftAtEvent(Guid KraftId, string Name, Guid EventId, decimal CommissionPercent);
