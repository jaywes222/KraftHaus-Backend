using Common.Domain.StronglyTypedIds;

namespace Identity.Domain.Common;

public readonly record struct OtpChallengeId(Guid Value) : IStronglyTypedId;

public readonly record struct AuthSessionId(Guid Value) : IStronglyTypedId;

public readonly record struct RoleAssignmentId(Guid Value) : IStronglyTypedId;

public readonly record struct StaffInvitationId(Guid Value) : IStronglyTypedId;

public readonly record struct CustomerRefId(Guid Value) : IStronglyTypedId;
public readonly record struct ConsentRecordId(DefaultIdType Value) : IStronglyTypedId;
