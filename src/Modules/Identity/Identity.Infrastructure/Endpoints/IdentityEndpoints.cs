using Common.Application.Auth;
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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace Identity.Infrastructure.Endpoints;

internal static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/identity")
            .WithTags("Identity");

        MapOtpEndpoints(group);
        MapSessionEndpoints(group);
        group.MapStaffEndpoints();
    }

    private static void MapOtpEndpoints(
        RouteGroupBuilder group)
    {
        group
            .MapPost("/otp/request", RequestOtp)
            .RequireRateLimiting("Sms")
            .AddEndpointFilter<
                ResultToResponseTransformer<RequestOtpResult>>();

        group
            .MapPost("/otp/verify", VerifyOtp)
            .RequireRateLimiting("OtpVerify")
            .AddEndpointFilter<
                ResultToResponseTransformer<CompleteOtpLoginResult>>();
    }

    private static void MapSessionEndpoints(
        RouteGroupBuilder group)
    {
        group
            .MapPost("/session/refresh", RefreshSession)
            .RequireRateLimiting("TokenRefresh")
            .AddEndpointFilter<
                ResultToResponseTransformer<RefreshAuthSessionResult>>();

        group
            .MapPost("/session/revoke", RevokeSession)
            .RequireAuthorization()
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

    /// <summary>
    ///     Signs out the caller's own session. The session id comes from the validated access token (<c>sid</c>),
    ///     never from the request, so one user can never revoke another user's session.
    /// </summary>
    private static Task<Result> RevokeSession(
        ICurrentUser currentUser,
        RevokeAuthSessionService service,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id.IsEmpty ||
            !Guid.TryParse(currentUser.SessionId, out var sessionId))
        {
            return Task.FromResult<Result>(
                IdentityErrors.Unauthorized(
                    "SessionRequired",
                    "A session-bound access token is required."));
        }

        return service.ExecuteAsync(
            new RevokeAuthSessionCommand(
                new AuthSessionId(sessionId),
                currentUser.Id),
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
    string DisplayName,
    bool AcceptTerms,
    bool AcceptPrivacy);

internal sealed record RefreshSessionRequest(
    AuthSessionId SessionId,
    string RefreshToken);
