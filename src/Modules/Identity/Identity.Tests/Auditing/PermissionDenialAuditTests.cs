using System.Net;
using System.Security.Claims;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Auditing;
using Identity.Application.Authorization;
using Identity.Domain.Authorization;
using Identity.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Identity.Tests.Auditing;

public sealed class PermissionDenialAuditTests
{
    [Fact]
    public async Task HandleAsync_PermissionMissing_RecordsAuthorizationDenied()
    {
        var userId = Guid.CreateVersion7();
        var audit = new RecordingIdentityAuditTrail();
        var permissions = new Mock<IPermissionAuthorizationService>();

        permissions
            .Setup(x => x.HasPermissionAsync(
                It.IsAny<ApplicationUserId>(),
                It.IsAny<string>(),
                It.IsAny<PermissionScope>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new PermissionAuthorizationHandler(permissions.Object, audit);
        var requirement = new PermissionRequirement(IdentityPermission.UsersView);
        var context = new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test")),
            new DefaultHttpContext());

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);

        var rejection = Assert.Single(audit.Rejections);
        Assert.Equal(IdentityAuditActions.AuthorizationDenied, rejection.Action);
        Assert.Equal("Identity.PermissionDenied", rejection.Error.Key);
        Assert.Equal(HttpStatusCode.Forbidden, rejection.Error.StatusCode);
        Assert.Equal(userId, rejection.SubjectUserId);
        Assert.Equal(IdentityPermission.UsersView, rejection.Target!.Id);
        Assert.Equal("Platform", rejection.Metadata!["scopeType"]);
    }

    [Fact]
    public async Task HandleAsync_PermissionGranted_RecordsNothing()
    {
        var audit = new RecordingIdentityAuditTrail();
        var permissions = new Mock<IPermissionAuthorizationService>();

        permissions
            .Setup(x => x.HasPermissionAsync(
                It.IsAny<ApplicationUserId>(),
                It.IsAny<string>(),
                It.IsAny<PermissionScope>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new PermissionAuthorizationHandler(permissions.Object, audit);
        var requirement = new PermissionRequirement(IdentityPermission.UsersView);
        var context = new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.CreateVersion7().ToString())], "test")),
            new DefaultHttpContext());

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        Assert.Empty(audit.Rejections);
    }
}
