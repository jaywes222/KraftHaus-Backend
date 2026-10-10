using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;

namespace Identity.Domain.Users;

public sealed record UserSnapshot(
    ApplicationUserId UserId,
    IdentityScope Scope,
    string? TenantId,
    string? Phone,
    string? Email,
    string? DisplayName,
    UserStatus Status);
