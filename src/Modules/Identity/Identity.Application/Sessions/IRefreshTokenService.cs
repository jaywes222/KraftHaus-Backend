using Identity.Domain.Common;

namespace Identity.Application.Sessions;

public interface IRefreshTokenService
{
    string GenerateSecret();

    string Create(
        AuthSessionId sessionId,
        long generation,
        string secret);

    bool TryParse(
        string refreshToken,
        out RefreshTokenParts parts);

    string HashSecret(string secret);

    bool VerifySecret(
        string secret,
        string expectedHash);
}

public readonly record struct RefreshTokenParts(
    AuthSessionId SessionId,
    long Generation,
    string Secret);
