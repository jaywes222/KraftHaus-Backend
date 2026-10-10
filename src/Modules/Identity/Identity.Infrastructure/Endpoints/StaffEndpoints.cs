using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Invitations;
using Identity.Application.Staff;
using Identity.Application.Users;
using Identity.Domain.Authorization;
using Identity.Domain.Common;
using Identity.Domain.Invitations;
using Identity.Domain.Roles;
using Identity.Infrastructure.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Infrastructure.Endpoints;

/// <summary>
///     The signed-in caller (<c>/me</c>) and staff management on the host's tenant (UF-022): invitations and role
///     assignments. Every write is scoped to the tenant resolved from the host name; nothing in the request names a
///     tenant.
/// </summary>
internal static class StaffEndpoints
{
    public static void MapStaffEndpoints(this RouteGroupBuilder group)
    {
        group
            .MapGet("/me", GetCurrentUser)
            .RequireAuthorization()
            .AddEndpointFilter<ResultToResponseTransformer<CurrentUserResult>>();

        var staff = group.MapGroup("/staff");

        staff
            .MapGet("/invitations", ListInvitations)
            .RequirePermission(IdentityPermission.UsersView)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<StaffInvitationResult>>>();

        staff
            .MapPost("/invitations", CreateInvitation)
            .RequirePermission(IdentityPermission.InvitationsCreate)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<StaffInvitationResult>>();

        staff
            .MapPost("/invitations/{invitationId:guid}/revoke", RevokeInvitation)
            .RequirePermission(IdentityPermission.InvitationsRevoke)
            .AddEndpointFilter<ResultToResponseTransformer>();

        // Anonymous: the invitee proves the invited phone/e-mail with a StaffInvitation OTP.
        staff
            .MapPost("/invitations/{invitationId:guid}/accept", AcceptInvitation)
            .RequireRateLimiting("OtpVerify")
            .AddEndpointFilter<ResultToResponseTransformer<AcceptStaffInvitationResult>>();

        staff
            .MapGet("/users/{userId:guid}/roles", ListUserRoles)
            .RequirePermission(IdentityPermission.RolesView)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<RoleAssignmentResult>>>();

        staff
            .MapPost("/users/{userId:guid}/roles", GrantRole)
            .RequirePermission(IdentityPermission.RolesGrant)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<RoleAssignmentResult>>();

        staff
            .MapPost("/roles/{assignmentId:guid}/revoke", RevokeRole)
            .RequirePermission(IdentityPermission.RolesRevoke)
            .AddEndpointFilter<ResultToResponseTransformer>();
    }

    private static Task<Result<CurrentUserResult>> GetCurrentUser(
        ICurrentUser currentUser,
        GetCurrentUserService service,
        CancellationToken cancellationToken) =>
        service.ExecuteAsync(currentUser.Id, currentUser.SessionId, cancellationToken);

    private static Task<Result<IReadOnlyList<StaffInvitationResult>>> ListInvitations(
        ListStaffInvitationsService service,
        CancellationToken cancellationToken) =>
        service.ExecuteAsync(cancellationToken);

    private static Task<Result<StaffInvitationResult>> CreateInvitation(
        CreateInvitationRequest request,
        ICurrentUser currentUser,
        CreateStaffInvitationService service,
        CancellationToken cancellationToken) =>
        service.ExecuteAsync(
            new CreateStaffInvitationCommand(
                currentUser.Id,
                request.Channel,
                request.Destination,
                request.Role,
                request.KraftId),
            cancellationToken);

    private static Task<Result> RevokeInvitation(
        Guid invitationId,
        RevokeStaffInvitationService service,
        CancellationToken cancellationToken) =>
        service.ExecuteAsync(
            new RevokeStaffInvitationCommand(new StaffInvitationId(invitationId)),
            cancellationToken);

    private static Task<Result<AcceptStaffInvitationResult>> AcceptInvitation(
        Guid invitationId,
        AcceptInvitationRequest request,
        AcceptStaffInvitationService service,
        CancellationToken cancellationToken) =>
        service.ExecuteAsync(
            new AcceptStaffInvitationCommand(new StaffInvitationId(invitationId), request.Code),
            cancellationToken);

    private static Task<Result<IReadOnlyList<RoleAssignmentResult>>> ListUserRoles(
        Guid userId,
        ListUserRolesService service,
        CancellationToken cancellationToken) =>
        service.ExecuteAsync(new ApplicationUserId(userId), cancellationToken);

    private static Task<Result<RoleAssignmentResult>> GrantRole(
        Guid userId,
        GrantRoleRequest request,
        GrantRoleService service,
        CancellationToken cancellationToken) =>
        service.ExecuteAsync(
            new GrantRoleCommand(new ApplicationUserId(userId), request.Role, request.KraftId),
            cancellationToken);

    private static Task<Result> RevokeRole(
        Guid assignmentId,
        RevokeRoleService service,
        CancellationToken cancellationToken) =>
        service.ExecuteAsync(
            new RevokeRoleCommand(new RoleAssignmentId(assignmentId)),
            cancellationToken);
}

internal sealed record CreateInvitationRequest(
    InvitationChannel Channel,
    string Destination,
    IdentityRole Role,
    Guid? KraftId);

internal sealed record AcceptInvitationRequest(string Code);

internal sealed record GrantRoleRequest(
    IdentityRole Role,
    Guid? KraftId);
