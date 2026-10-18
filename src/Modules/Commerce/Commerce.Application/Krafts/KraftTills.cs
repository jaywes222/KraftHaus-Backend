using Commerce.Domain.Krafts;
using Common.Application.Payments;

namespace Commerce.Application.Krafts;

/// <summary>
///     Where a Kraft's customers pay at an event: its own approved till, or the platform's till when the Kraft's terms
///     say so (no till of its own needed). A Kraft can sell when either is in place.
/// </summary>
public sealed class KraftTills(IPaymentDestinations destinations)
{
    /// <summary>Krafts among <paramref name="krafts" /> that can take payments at the event.</summary>
    public async Task<IReadOnlySet<Guid>> SellingAsync(Guid eventId, IEnumerable<Kraft> krafts, CancellationToken cancellationToken)
    {
        var approved = await destinations.KraftsWithApprovedTillAsync(eventId, cancellationToken);
        var selling = new HashSet<Guid>(approved);

        foreach (var kraft in krafts)
        {
            if (kraft.AtEvent(eventId) is { PaysIntoPlatformTill: true })
            {
                selling.Add(kraft.Id.Value);
            }
        }

        return selling;
    }

    /// <summary>
    ///     The destination for a prompt: <c>(true, null)</c> means the platform's own till, <c>(true, id)</c> the
    ///     Kraft's till, <c>(false, null)</c> that the Kraft cannot take payments now.
    /// </summary>
    public async Task<(bool CanPay, Guid? DestinationId)> DestinationAsync(Kraft kraft, Guid eventId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kraft);

        if (kraft.AtEvent(eventId) is not { } terms)
        {
            return (false, null);
        }

        if (terms.PaysIntoPlatformTill)
        {
            return (true, null);
        }

        var till = await destinations.FindApprovedTillAsync(kraft.Id.Value, eventId, cancellationToken);
        return (till is not null, till);
    }
}
