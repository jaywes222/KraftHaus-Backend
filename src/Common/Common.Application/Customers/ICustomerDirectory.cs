using Common.Domain.StronglyTypedIds;

namespace Common.Application.Customers;

/// <summary>
///     The signed-in customer as other modules need them (Spec 08 §5.2): their verified phone and display name, for
///     the host tenant's identity realm only. Implemented by Identity.
/// </summary>
public interface ICustomerDirectory
{
    /// <summary><c>null</c> when the user does not exist, is disabled, or belongs to another tenant's realm.</summary>
    Task<CustomerProfile?> FindAsync(ApplicationUserId userId, CancellationToken cancellationToken);
}

/// <param name="Phone">E.164, or <c>null</c> for e-mail-only accounts (staff).</param>
public sealed record CustomerProfile(ApplicationUserId UserId, string? Phone, string DisplayName);
