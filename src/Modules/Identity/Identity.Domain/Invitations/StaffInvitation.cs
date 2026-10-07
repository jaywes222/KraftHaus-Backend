using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Events;
using Identity.Domain.Roles;
using Identity.Domain.Users;

namespace Identity.Domain.Invitations;

public sealed class StaffInvitation(StaffInvitationId id)
    : AggregateRoot<StaffInvitationId>(id)
{
    private static readonly TimeSpan _invitationLifetime =
        TimeSpan.FromDays(7);

    private StaffInvitation()
        : this(new StaffInvitationId(
            Guid.CreateVersion7()))
    {
    }

    public ApplicationUserId InvitedByUserId { get; private set; }

    public InvitationChannel Channel { get; private set; }

    public string Destination { get; private set; } =
        string.Empty;

    public IdentityRole Role { get; private set; }

    public RoleScopeType ScopeType { get; private set; }

    public Guid? ScopeId { get; private set; }

    public StaffInvitationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsExpired(DateTimeOffset now) =>
        Status == StaffInvitationStatus.Pending &&
        now >= CreatedAt + _invitationLifetime;

    public static Result<StaffInvitation> ForPhone(
        StaffInvitationId id,
        ApplicationUserId invitedByUserId,
        string destination,
        IdentityRole role,
        RoleScopeType scopeType,
        Guid? scopeId,
        DateTimeOffset now)
    {
        return Create(
            id,
            invitedByUserId,
            InvitationChannel.Phone,
            destination,
            role,
            scopeType,
            scopeId,
            now);
    }

    public static Result<StaffInvitation> ForEmail(
        StaffInvitationId id,
        ApplicationUserId invitedByUserId,
        string destination,
        IdentityRole role,
        RoleScopeType scopeType,
        Guid? scopeId,
        DateTimeOffset now)
    {
        return Create(
            id,
            invitedByUserId,
            InvitationChannel.Email,
            destination,
            role,
            scopeType,
            scopeId,
            now);
    }

    private static Result<StaffInvitation> Create(
        StaffInvitationId id,
        ApplicationUserId invitedByUserId,
        InvitationChannel channel,
        string destination,
        IdentityRole role,
        RoleScopeType scopeType,
        Guid? scopeId,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return new Error
            {
                Key = "Identity.InvitationDestinationRequired",
                Description =
                    "Invitation destination is required."
            };
        }

        var scopeResult =
            RoleScopeRules.Validate(
                role,
                scopeType,
                scopeId);

        if (scopeResult.IsFailure)
        {
            return scopeResult.Error!;
        }

        var invitation = new StaffInvitation(id)
        {
            InvitedByUserId = invitedByUserId,
            Channel = channel,
            Destination = destination.Trim(),
            Role = role,
            ScopeType = scopeType,
            ScopeId = scopeId,
            Status = StaffInvitationStatus.Pending,
            CreatedAt = now
        };

        invitation.RaiseEvent(
            new StaffInvited(
                invitation.Id,
                invitation.InvitedByUserId,
                invitation.Channel,
                invitation.Destination,
                invitation.Role,
                invitation.ScopeType,
                invitation.ScopeId));

        return invitation;
    }

    public Result Accept(
        ApplicationUser user,
        DateTimeOffset now)
    {
        if (Status != StaffInvitationStatus.Pending)
        {
            return new Error
            {
                Key = "Identity.InvitationNotPending",
                Description =
                    "The invitation is no longer pending."
            };
        }

        if (IsExpired(now))
        {
            return new Error
            {
                Key = "Identity.InvitationExpired",
                Description =
                    "The invitation has expired."
            };
        }

        if (user.Status != UserStatus.Active)
        {
            return new Error
            {
                Key = "Identity.InvitationUserDisabled",
                Description =
                    "The invited user is disabled."
            };
        }

        var ownsDestination =
            Channel == InvitationChannel.Phone
                ? user.Phone?.Value == Destination
                : user.Email?.Value == Destination;

        if (!ownsDestination)
        {
            return new Error
            {
                Key = "Identity.InvitationDestinationMismatch",
                Description =
                    "The verified identity does not match the invitation destination."
            };
        }

        Status = StaffInvitationStatus.Accepted;
        AcceptedAt = now;

        RaiseEvent(
            new StaffInvitationAccepted(
                Id,
                user.Id,
                Role,
                ScopeType,
                ScopeId));

        return Result.Success;
    }

    public Result Revoke(DateTimeOffset now)
    {
        if (Status != StaffInvitationStatus.Pending)
        {
            return new Error
            {
                Key = "Identity.InvitationNotPending",
                Description =
                    "Only pending invitations can be revoked."
            };
        }

        Status = StaffInvitationStatus.Revoked;
        RevokedAt = now;

        RaiseEvent(
            new StaffInvitationRevoked(Id));

        return Result.Success;
    }
}
