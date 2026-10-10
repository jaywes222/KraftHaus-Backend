using System.Security.Claims;
using System.Timers;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Authorization;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Identity.Infrastructure.Authorization;
using Identity.Tests.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Moq;
using Xunit;

namespace Identity.Tests;

public sealed class PermissionAuthorizationTests
{
    [Fact]
    public async Task AuthenticatedUserWithPermissionAndCorrectPlatformScopeIsAllowed()
    {
        var userId = NewUserId();

        var permissions = new Mock<IPermissionAuthorizationService>();

        permissions
            .Setup(x =>
                x.HasPermissionAsync(
                    userId,
                    IdentityPermission.UsersView,
                    PermissionScope.Platform(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler =
            new PermissionAuthorizationHandler(
                permissions.Object,
                new RecordingIdentityAuditTrail());

        var context =
            CreateContext(
                userId,
                IdentityPermission.UsersView);

        await handler.HandleAsync(context);

        Assert.True(
            context.HasSucceeded);
    }

    [Fact]
    public async Task AuthenticatedUserWithoutPermissionIsDenied()
    {
        var userId = NewUserId();

        var permissions = new Mock<IPermissionAuthorizationService>();

        permissions
            .Setup(x =>
                x.HasPermissionAsync(
                    userId,
                    IdentityPermission.UsersManage,
                    PermissionScope.Platform(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler =
            new PermissionAuthorizationHandler(
                permissions.Object,
                new RecordingIdentityAuditTrail());

        var context =
            CreateContext(
                userId,
                IdentityPermission.UsersManage);

        await handler.HandleAsync(context);

        Assert.False(
            context.HasSucceeded);
    }

    [Fact]
    public async Task AuthenticatedUserWithPermissionForWrongScopeIsDenied()
    {
        var userId = NewUserId();

        var requestedKraftId =
            Guid.CreateVersion7();

        var permissions = new Mock<IPermissionAuthorizationService>();

        permissions
            .Setup(x =>
                x.HasPermissionAsync(
                    userId,
                    IdentityPermission.KitchenHandle,
                    PermissionScope.Kraft(
                        requestedKraftId),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler =
            new PermissionAuthorizationHandler(
                permissions.Object,
                new RecordingIdentityAuditTrail());

        var context =
            CreateContext(
                userId,
                IdentityPermission.KitchenHandle,
                kraftId: requestedKraftId);

        await handler.HandleAsync(context);

        Assert.False(
            context.HasSucceeded);
    }

    [Fact]
    public async Task FinanceCanApproveTill()
    {
        var userId = NewUserId();

        var tenantId =
            Guid.CreateVersion7();

        var permissions = new Mock<IPermissionAuthorizationService>();

        permissions
            .Setup(x =>
                x.HasPermissionAsync(
                    userId,
                    IdentityPermission.TillApprove,
                    PermissionScope.Tenant(
                        tenantId),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler =
            new PermissionAuthorizationHandler(
                permissions.Object,
                new RecordingIdentityAuditTrail());

        var context =
            CreateContext(
                userId,
                IdentityPermission.TillApprove,
                tenantId: tenantId);

        await handler.HandleAsync(context);

        Assert.True(
            context.HasSucceeded);
    }

    [Fact]
    public async Task FinanceCannotHandleKitchen()
    {
        var userId = NewUserId();

        var tenantId =
            Guid.CreateVersion7();

        var permissions = new Mock<IPermissionAuthorizationService>();

        permissions
            .Setup(x =>
                x.HasPermissionAsync(
                    userId,
                    IdentityPermission.KitchenHandle,
                    PermissionScope.Tenant(
                        tenantId),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler =
            new PermissionAuthorizationHandler(
                permissions.Object,
                new RecordingIdentityAuditTrail());

        var context =
            CreateContext(
                userId,
                IdentityPermission.KitchenHandle,
                tenantId: tenantId);

        await handler.HandleAsync(context);

        Assert.False(
            context.HasSucceeded);
    }

    [Fact]
    public async Task KraftStaffCanHandleKitchenForAssignedKraft()
    {
        var userId = NewUserId();

        var kraftId =
            Guid.CreateVersion7();

        var permissions = new Mock<IPermissionAuthorizationService>();

        permissions
            .Setup(x =>
                x.HasPermissionAsync(
                    userId,
                    IdentityPermission.KitchenHandle,
                    PermissionScope.Kraft(
                        kraftId),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler =
            new PermissionAuthorizationHandler(
                permissions.Object,
                new RecordingIdentityAuditTrail());

        var context =
            CreateContext(
                userId,
                IdentityPermission.KitchenHandle,
                kraftId: kraftId);

        await handler.HandleAsync(context);

        Assert.True(
            context.HasSucceeded);
    }

    [Fact]
    public async Task KraftStaffCannotHandleKitchenForAnotherKraft()
    {
        var userId = NewUserId();

        var requestedKraftId =
            Guid.CreateVersion7();

        var permissions = new Mock<IPermissionAuthorizationService>();

        permissions
            .Setup(x =>
                x.HasPermissionAsync(
                    userId,
                    IdentityPermission.KitchenHandle,
                    PermissionScope.Kraft(
                        requestedKraftId),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler =
            new PermissionAuthorizationHandler(
                permissions.Object,
                new RecordingIdentityAuditTrail());

        var context =
            CreateContext(
                userId,
                IdentityPermission.KitchenHandle,
                kraftId: requestedKraftId);

        await handler.HandleAsync(context);

        Assert.False(
            context.HasSucceeded);

        permissions.Verify(
            x =>
                x.HasPermissionAsync(
                    userId,
                    IdentityPermission.KitchenHandle,
                    PermissionScope.Kraft(
                        requestedKraftId),
                    It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MissingSubjectClaimIsDenied()
    {
        var permissions = new Mock<IPermissionAuthorizationService>();

        var handler =
            new PermissionAuthorizationHandler(
                permissions.Object,
                new RecordingIdentityAuditTrail());

        var context =
            new AuthorizationHandlerContext(
                [
                    new PermissionRequirement(
                        IdentityPermission.UsersView)
                ],
                new ClaimsPrincipal(
                    new ClaimsIdentity(
                        authenticationType: "Bearer")),
                new DefaultHttpContext());

        await handler.HandleAsync(context);

        Assert.False(
            context.HasSucceeded);

        permissions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvalidSubjectClaimIsDenied()
    {
        var permissions = new Mock<IPermissionAuthorizationService>();

        var handler =
            new PermissionAuthorizationHandler(
                permissions.Object,
                new RecordingIdentityAuditTrail());

        var context =
            new AuthorizationHandlerContext(
                [
                    new PermissionRequirement(
                        IdentityPermission.UsersView)
                ],
                new ClaimsPrincipal(
                    new ClaimsIdentity(
                    [
                        new Claim(
                            "sub",
                            "not-a-guid")
                    ],
                    "Bearer")),
                new DefaultHttpContext());

        await handler.HandleAsync(context);

        Assert.False(
            context.HasSucceeded);

        permissions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task KraftRouteScopeTakesPrecedenceOverTenantScope()
    {
        var userId = NewUserId();

        var kraftId =
            Guid.CreateVersion7();

        var tenantId =
            Guid.CreateVersion7();

        var permissions = new Mock<IPermissionAuthorizationService>();

        permissions
            .Setup(x =>
                x.HasPermissionAsync(
                    userId,
                    IdentityPermission.KitchenHandle,
                    PermissionScope.Kraft(kraftId),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler =
            new PermissionAuthorizationHandler(
                permissions.Object,
                new RecordingIdentityAuditTrail());

        var context =
            CreateContext(
                userId,
                IdentityPermission.KitchenHandle,
                tenantId: tenantId,
                kraftId: kraftId);

        await handler.HandleAsync(context);

        Assert.True(
            context.HasSucceeded);

        permissions.Verify(
            x =>
                x.HasPermissionAsync(
                    userId,
                    IdentityPermission.KitchenHandle,
                    PermissionScope.Kraft(kraftId),
                    It.IsAny<CancellationToken>()),
            Times.Once);

        permissions.Verify(
            x =>
                x.HasPermissionAsync(
                    userId,
                    IdentityPermission.KitchenHandle,
                    PermissionScope.Tenant(tenantId),
                    It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TenantRouteScopeIsUsedWhenNoKraftRouteValueExists()
    {
        var userId = NewUserId();

        var tenantId =
            Guid.CreateVersion7();

        var permissions = new Mock<IPermissionAuthorizationService>();

        permissions
            .Setup(x =>
                x.HasPermissionAsync(
                    userId,
                    IdentityPermission.PaymentsView,
                    PermissionScope.Tenant(tenantId),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler =
            new PermissionAuthorizationHandler(
                permissions.Object,
                new RecordingIdentityAuditTrail());

        var context =
            CreateContext(
                userId,
                IdentityPermission.PaymentsView,
                tenantId: tenantId);

        await handler.HandleAsync(context);

        Assert.True(
            context.HasSucceeded);
    }

    [Fact]
    public async Task UnknownPermissionIsNotForwardedToAuthorizationService()
    {
        var userId = NewUserId();

        var permissions = new Mock<IPermissionAuthorizationService>();

        var handler =
            new PermissionAuthorizationHandler(
                permissions.Object,
                new RecordingIdentityAuditTrail());

        var context =
            CreateContext(
                userId,
                "identity:not-real");

        await handler.HandleAsync(context);

        Assert.False(
            context.HasSucceeded);

        permissions.VerifyNoOtherCalls();
    }

    private static AuthorizationHandlerContext CreateContext(
        ApplicationUserId userId,
        string permission,
        Guid? tenantId = null,
        Guid? kraftId = null)
    {
        var httpContext =
            new DefaultHttpContext();

        httpContext.Request.RouteValues =
            new RouteValueDictionary();

        if (tenantId.HasValue)
        {
            httpContext.Request.RouteValues["tenantId"] =
                tenantId.Value;
        }

        if (kraftId.HasValue)
        {
            httpContext.Request.RouteValues["kraftId"] =
                kraftId.Value;
        }

        var principal =
            new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(
                        "sub",
                        userId.Value.ToString())
                ],
                "Bearer"));

        return new AuthorizationHandlerContext(
            [
                new PermissionRequirement(
                    permission)
            ],
            principal,
            httpContext);
    }

    private static ApplicationUserId NewUserId()
    {
        return new ApplicationUserId(
            Guid.CreateVersion7());
    }
}
