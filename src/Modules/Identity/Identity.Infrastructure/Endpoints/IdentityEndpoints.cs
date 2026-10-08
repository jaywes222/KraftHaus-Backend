using Common.Application.EndpointFilters;
using Common.Domain.ResultMonad;
using Identity.Application.Auth;
using Identity.Application.Otp;
using Identity.Application.Sessions;
using Identity.Domain.Common;
using Identity.Domain.Otp;
using Identity.Domain.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tenancy.Domain.Common;
using Tenancy.Domain.Tenants;

namespace Identity.Infrastructure.Endpoints;

internal static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group =
            endpoints
                .MapGroup("/api/identity")
                .WithTags("Identity");

        MapOtpEndpoints(group);
        MapSessionEndpoints(group);
    }

    private static void MapOtpEndpoints(
        RouteGroupBuilder group)
    {
        group
            .MapPost(
                "/otp/request",
                RequestOtp)
            .AddEndpointFilter<ResultToResponseTransformer<RequestOtpResult>>();

        group
            .MapPost(
                "/otp/verify",
                VerifyOtp)
            .AddEndpointFilter<ResultToResponseTransformer<CompleteOtpLoginResult>>();
    }

    private static void MapSessionEndpoints(
        RouteGroupBuilder group)
    {
        group
            .MapPost(
                "/session/refresh",
                RefreshSession)
            .AddEndpointFilter<ResultToResponseTransformer<RefreshAuthSessionResult>>();

        group
            .MapPost(
                "/session/revoke",
                RevokeSession)
            .AddEndpointFilter<ResultToResponseTransformer>();
    }

    private static Task<Result<RequestOtpResult>> RequestOtp(
        RequestOtpRequest request,
        RequestOtpService service,
        CancellationToken cancellationToken)
    {
        return service.ExecuteAsync(
            new RequestOtpCommand(
                request.Channel,
                request.Destination,
                request.Purpose),
            cancellationToken);
    }

    private static Task<Result<CompleteOtpLoginResult>> VerifyOtp(
        VerifyOtpRequest request,
        CompleteOtpLoginService service,
        CancellationToken cancellationToken)
    {
        return service.ExecuteAsync(
            new CompleteOtpLoginCommand(
                request.ChallengeId,
                request.Code,
                request.DeviceId,
                request.Scope,
                request.TenantId,
                request.DisplayName,
                request.AcceptTerms,
                request.AcceptPrivacy),
            cancellationToken);
    }

    private static Task<Result<RefreshAuthSessionResult>> RefreshSession(
        RefreshSessionRequest request,
        RefreshAuthSessionService service,
        CancellationToken cancellationToken)
    {
        return service.ExecuteAsync(
            new RefreshAuthSessionCommand(
                request.SessionId,
                request.RefreshToken),
            cancellationToken);
    }

    private static Task<Result> RevokeSession(
        RevokeSessionRequest request,
        RevokeAuthSessionService service,
        CancellationToken cancellationToken)
    {
        return service.ExecuteAsync(
            new RevokeAuthSessionCommand(
                request.SessionId),
            cancellationToken);
    }
}

internal sealed record RequestOtpRequest(
    OtpChannel Channel,
    string Destination,
    OtpPurpose Purpose);

internal sealed record VerifyOtpRequest(
    OtpChallengeId ChallengeId,
    string Code,
    string DeviceId,
    IdentityScope Scope,
    TenantId? TenantId,
    string DisplayName,
    bool AcceptTerms,
    bool AcceptPrivacy);

internal sealed record RefreshSessionRequest(
    AuthSessionId SessionId,
    string RefreshToken);

internal sealed record RevokeSessionRequest(
    AuthSessionId SessionId);
