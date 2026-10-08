using System.Security.Claims;
using Common.Domain.StronglyTypedIds;
using Duende.IdentityModel;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Domain.Roles;
using Identity.Domain.Users;

namespace Identity.Infrastructure.IdentityServer;

public sealed class KraftHausProfileService(
    IIdentityUserRepository users,
    IRoleAssignmentRepository roleAssignments)
    : IProfileService
{
    public async Task GetProfileDataAsync(
        ProfileDataRequestContext context,
        CancellationToken ct)
    {
        var subjectId =
            context.Subject.FindFirst(
                JwtClaimTypes.Subject)?.Value;

        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return;
        }

        if (!ApplicationUserId.TryParse(
                subjectId,
                out var userId))
        {
            return;
        }

        var user =
            await users.FindAsync(
                userId,
                ct);

        if (user is null)
        {
            return;
        }

        var claims = new List<Claim>
        {
            new(
                JwtClaimTypes.Subject,
                user.Id.ToString()),

            new(
                "scope",
                user.Scope.ToString())
        };

        if (user.TenantId is not null)
        {
            claims.Add(
                new Claim(
                    "tid",
                    user.TenantId.Value.ToString()));
        }

        var sessionId =
            context.Subject.FindFirst(
                JwtClaimTypes.SessionId)?.Value;

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            claims.Add(
                new Claim(
                    JwtClaimTypes.SessionId,
                    sessionId));
        }

        var assignments =
            await roleAssignments.GetActiveForUserAsync(
                user.Id,
                ct);

        foreach (var assignment in assignments)
        {
            if (assignment.Status != RoleAssignmentStatus.Active)
            {
                continue;
            }

            claims.Add(
                new Claim(
                    "role",
                    assignment.Role.ToString()));
        }

        context.IssuedClaims.AddRange(claims);
    }

    public async Task IsActiveAsync(
        IsActiveContext context,
        CancellationToken ct)
    {
        var subjectId =
            context.Subject.FindFirst(
                JwtClaimTypes.Subject)?.Value;

        if (string.IsNullOrWhiteSpace(subjectId))
        {
            context.IsActive = false;
            return;
        }

        if (!ApplicationUserId.TryParse(
                subjectId,
                out var userId))
        {
            context.IsActive = false;
            return;
        }

        var user =
            await users.FindAsync(
                userId,
                ct);

        context.IsActive =
            user is not null &&
            user.Status == UserStatus.Active;
    }
}
