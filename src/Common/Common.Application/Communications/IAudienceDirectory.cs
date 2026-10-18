using Common.Domain.Communications;

namespace Common.Application.Communications;

/// <summary>Phones (E.164) for a broadcast audience, de-duplicated. Implemented by Ticketing.</summary>
public interface IAudienceDirectory
{
    Task<IReadOnlyList<AudienceMember>> ListAsync(Guid eventId, BroadcastAudience audience, CancellationToken cancellationToken);
}

/// <param name="Phone">E.164.</param>
/// <param name="FirstName">For a <c>{firstName}</c> placeholder; <c>null</c> when unknown.</param>
public sealed record AudienceMember(string Phone, string? FirstName);
