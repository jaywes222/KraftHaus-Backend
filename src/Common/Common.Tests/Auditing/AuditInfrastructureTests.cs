using System.Diagnostics;
using System.Security.Claims;
using Common.Application.Auditing;
using Common.Application.Auth;
using Common.Application.Options;
using Common.Application.Tenancy;
using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Common.Infrastructure.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Common.Tests.Auditing;

public sealed class AuditInfrastructureTests
{
    private const string Secret = "unit-test-audit-fingerprint-secret-0123456789";

    [Fact]
    public void Fingerprint_SameValueDifferentCasingAndWhitespace_IsStableHexAndNotTheValue()
    {
        var fingerprinter = CreateFingerprinter(Secret);

        var a = fingerprinter.Fingerprint("User@Example.com ");
        var b = fingerprinter.Fingerprint("user@example.com");

        Assert.Equal(a, b);
        Assert.Matches("^[0-9A-F]{64}$", a);
        Assert.DoesNotContain("example", a, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fingerprint_DifferentSecret_ProducesDifferentFingerprint()
    {
        var a = CreateFingerprinter(Secret).Fingerprint("+254712345678");
        var b = CreateFingerprinter(Secret + "-rotated").Fingerprint("+254712345678");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SecurityAuditOptionsValidator_ShortSecret_IsRejected()
    {
        var validator = new SecurityAuditOptionsValidator();

        var result = validator.Validate(new SecurityAuditOptions { FingerprintSecret = "too-short" });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetCurrent_NoHttpContext_IsSystemActorWithTraceId()
    {
        using var activity = new Activity("audit-test");
        activity.Start();

        var provider = new HttpAuditRequestContextProvider(
            new HttpContextAccessor(),
            new StubCurrentUser(null, null));

        var context = provider.GetCurrent();

        Assert.Equal(AuditActorType.System, context.Actor.Type);
        Assert.Null(context.Actor.Id);
        Assert.Equal(activity.TraceId.ToHexString(), context.TraceId);
        Assert.Null(context.RequestId);
    }

    [Fact]
    public void GetCurrent_AnonymousRequest_IsAnonymousActorWithRequestId()
    {
        var http = new DefaultHttpContext { TraceIdentifier = "req-1" };

        var provider = new HttpAuditRequestContextProvider(
            new HttpContextAccessor { HttpContext = http },
            new StubCurrentUser(null, null));

        var context = provider.GetCurrent();

        Assert.Equal(AuditActorType.Anonymous, context.Actor.Type);
        Assert.Null(context.Actor.Id);
        Assert.Equal("req-1", context.RequestId);
        Assert.Null(context.TenantId);
    }

    [Fact]
    public void GetCurrent_AuthenticatedRequest_UsesSubjectSessionAndResolvedTenant()
    {
        var userId = Guid.CreateVersion7();
        var resolvedTenant = Guid.CreateVersion7();
        var claimTenant = Guid.CreateVersion7();

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim("tid", claimTenant.ToString())], "test"))
        };

        var provider = new HttpAuditRequestContextProvider(
            new HttpContextAccessor { HttpContext = http },
            new StubCurrentUser(userId, "session-1"),
            new StubTenant(resolvedTenant));

        var context = provider.GetCurrent();

        Assert.Equal(AuditActorType.User, context.Actor.Type);
        Assert.Equal(userId.ToString(), context.Actor.Id);
        Assert.Equal("session-1", context.Actor.SessionId);
        Assert.Equal(resolvedTenant, context.TenantId);
    }

    [Fact]
    public void GetCurrent_NoResolvedTenant_FallsBackToTidClaim()
    {
        var claimTenant = Guid.CreateVersion7();

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim("tid", claimTenant.ToString())], "test"))
        };

        var provider = new HttpAuditRequestContextProvider(
            new HttpContextAccessor { HttpContext = http },
            new StubCurrentUser(Guid.CreateVersion7(), null),
            new StubTenant(null));

        Assert.Equal(claimTenant, provider.GetCurrent().TenantId);
    }

    private static HmacAuditFingerprinter CreateFingerprinter(string secret) =>
        new(Options.Create(new SecurityAuditOptions { FingerprintSecret = secret }));

    private sealed class StubCurrentUser(Guid? id, string? sessionId) : ICurrentUser
    {
        public ApplicationUserId Id => new(id ?? Guid.Empty);

        public string? IdAsString => id?.ToString();

        public ICollection<string> Roles => [];

        public string? SessionId => sessionId;
    }

    private sealed class StubTenant(Guid? tenantId) : ICurrentTenant
    {
        public bool IsResolved => tenantId.HasValue;

        public TenantId TenantId => new(tenantId ?? Guid.Empty);

        public IdentityScope IdentityScope => IdentityScope.Tenant;
    }
}
