using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Domain.Common;
using Identity.Domain.Invitations;
using Identity.Domain.Otp;
using Identity.Domain.Roles;
using Identity.Domain.Users;

namespace Identity.Application.Invitations;

public sealed record AcceptStaffInvitationCommand(
    StaffInvitationId InvitationId,
    string Code);

public sealed record AcceptStaffInvitationResult(
    ApplicationUserId UserId,
    IdentityRole Role,
    RoleScopeType ScopeType,
    Guid? ScopeId);

public sealed class AcceptStaffInvitationService(
    IStaffInvitationRepository invitations,
    IIdentityUserRepository users,
    IRoleAssignmentRepository assignments,
    IOtpProofService otpProof,
    IIdentityUnitOfWork unitOfWork)
{
    public async Task<Result<AcceptStaffInvitationResult>> ExecuteAsync(
        AcceptStaffInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var invitation =
            await invitations.FindAsync(
                command.InvitationId,
                cancellationToken);

        if (invitation is null)
        {
            return Error.NotFound(
                nameof(command.InvitationId),
                command.InvitationId);
        }

        var now = DateTimeOffset.UtcNow;

        if (invitation.Status != StaffInvitationStatus.Pending)
        {
            return new Error
            {
                Key = "Identity.InvitationNotPending",
                Description =
                    "The invitation is no longer pending."
            };
        }

        if (invitation.IsExpired(now))
        {
            return new Error
            {
                Key = "Identity.InvitationExpired",
                Description =
                    "The invitation has expired."
            };
        }

        var verified =
            await otpProof.VerifyAsync(
                MapChannel(invitation.Channel),
                invitation.Destination,
                command.Code,
                cancellationToken);

        if (!verified)
        {
            return new Error
            {
                Key = "Identity.InvitationOtpInvalid",
                Description =
                    "The verification code is invalid."
            };
        }

        var user =
            await FindUserAsync(
                invitation,
                cancellationToken);

        if (user is null)
        {
            return new Error
            {
                Key = "Identity.InvitationUserNotFound",
                Description =
                    "No application user exists for the verified identity."
            };
        }

        var acceptResult =
            invitation.Accept(
                user,
                now);

        if (acceptResult.IsFailure)
        {
            return acceptResult.Error!;
        }

        var roleResult =
            RoleAssignment.Grant(
                new RoleAssignmentId(
                    Guid.CreateVersion7()),
                user.Id,
                invitation.Role,
                invitation.ScopeType,
                invitation.ScopeId,
                now);

        if (roleResult.IsFailure)
        {
            return roleResult.Error!;
        }

        var assignment = roleResult.Value!;

        await invitations.SaveAsync(
            invitation,
            cancellationToken);

        await assignments.AddAsync(
            assignment,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return new AcceptStaffInvitationResult(
            user.Id,
            invitation.Role,
            invitation.ScopeType,
            invitation.ScopeId);
    }

    private async Task<ApplicationUser?> FindUserAsync(
        StaffInvitation invitation,
        CancellationToken cancellationToken)
    {
        return invitation.Channel switch
        {
            InvitationChannel.Phone =>
                await FindByPhoneAsync(
                    invitation.Destination,
                    cancellationToken),

            InvitationChannel.Email =>
                await FindByEmailAsync(
                    invitation.Destination,
                    cancellationToken),

            _ => null
        };
    }

    private async Task<ApplicationUser?> FindByPhoneAsync(
        string destination,
        CancellationToken cancellationToken)
    {
        var phone =
            PhoneNumber.Create(destination);

        if (phone.IsFailure)
        {
            return null;
        }

        return await users.FindByPhoneAsync(
            phone.Value!,
            cancellationToken);
    }

    private async Task<ApplicationUser?> FindByEmailAsync(
        string destination,
        CancellationToken cancellationToken)
    {
        var email =
            EmailAddress.Create(destination);

        if (email.IsFailure)
        {
            return null;
        }

        return await users.FindByEmailAsync(
            email.Value!,
            cancellationToken);
    }

    private static OtpChannel MapChannel(
        InvitationChannel channel)
    {
        return channel switch
        {
            InvitationChannel.Phone =>
                OtpChannel.Phone,

            InvitationChannel.Email =>
                OtpChannel.Email,

            _ => throw new ArgumentOutOfRangeException(
                nameof(channel))
        };
    }
}
