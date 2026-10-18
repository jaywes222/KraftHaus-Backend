namespace Common.Application.Ticketing;

/// <summary>Whether a phone is on an event's waitlist (implemented by Ticketing). Commerce uses it for the fee waiver.</summary>
public interface IWaitlistMembership
{
    /// <param name="phone">E.164.</param>
    Task<bool> IsOnWaitlistAsync(Guid eventId, string phone, CancellationToken cancellationToken);
}
