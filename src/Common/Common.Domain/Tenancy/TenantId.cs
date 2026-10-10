using Common.Domain.StronglyTypedIds;

namespace Common.Domain.Tenancy;

/// <summary>
///     Tenant identifier shared by every module (moved from Tenancy.Domain so modules such as Identity can refer to
///     tenants without referencing the Tenancy module).
/// </summary>
public readonly record struct TenantId(Guid Value) : IStronglyTypedId;
