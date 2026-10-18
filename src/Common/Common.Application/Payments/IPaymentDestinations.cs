namespace Common.Application.Payments;

/// <summary>
///     Where money goes (Spec 08 §7.1, implemented by Payments). Kraft sales are paid into the Kraft's own till for the
///     event (D-03); a till takes money only once Finance has approved it (UF-017).
/// </summary>
public interface IPaymentDestinations
{
    /// <summary>The approved till of a Kraft at an event, or null when it has none.</summary>
    Task<Guid?> FindApprovedTillAsync(Guid kraftId, Guid eventId, CancellationToken cancellationToken);

    /// <summary>Krafts at the event whose till is approved (they can sell).</summary>
    Task<IReadOnlySet<Guid>> KraftsWithApprovedTillAsync(Guid eventId, CancellationToken cancellationToken);
}
