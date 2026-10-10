using Common.Domain.StronglyTypedIds;

namespace Tenancy.Domain.Common;

public readonly record struct TenantHostId(Guid Value) : IStronglyTypedId;
public readonly record struct PolicyVersionId(Guid Value) : IStronglyTypedId;
public readonly record struct DatabaseIdentityId(Guid Value) : IStronglyTypedId;
