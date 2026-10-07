using Common.Domain.StronglyTypedIds;
using Tenancy.Domain.Tenants;

namespace Identity.Domain.Users;

public sealed record UserSnapshot(
    ApplicationUserId UserId,
    IdentityScope Scope,
    string? TenantId,
    string? Phone,
    string? Email,
    string? DisplayName,
    UserStatus Status);
