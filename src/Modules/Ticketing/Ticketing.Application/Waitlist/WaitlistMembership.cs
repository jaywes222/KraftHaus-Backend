using Common.Application.Ticketing;
using Ticketing.Application.Abstractions;

namespace Ticketing.Application.Waitlist;

/// <summary><see cref="IWaitlistMembership" /> for other modules (Commerce's fee waiver).</summary>
public sealed class WaitlistMembership(IWaitlistRepository waitlist) : IWaitlistMembership
{
    public Task<bool> IsOnWaitlistAsync(Guid eventId, string phone, CancellationToken cancellationToken) =>
        waitlist.ExistsAsync(eventId, phone, cancellationToken);
}
