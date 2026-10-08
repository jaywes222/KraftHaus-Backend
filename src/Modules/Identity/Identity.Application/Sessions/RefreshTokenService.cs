using System.Security.Cryptography;
using System.Text;
using Identity.Domain.Common;

namespace Identity.Application.Sessions;

public sealed class RefreshTokenService : IRefreshTokenService
{
    public string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public string Create(
        AuthSessionId sessionId,
        long generation,
        string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        return string.Join(
            '.',
            sessionId.Value,
            generation,
            secret);
    }

    public bool TryParse(
        string refreshToken,
        out RefreshTokenParts parts)
    {
        parts = default;

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        var segments =
            refreshToken.Split(
                '.',
                StringSplitOptions.None);

        if (segments.Length != 3)
        {
            return false;
        }

        if (!Guid.TryParse(
                segments[0],
                out var sessionId))
        {
            return false;
        }

        if (!long.TryParse(
                segments[1],
                out var generation) ||
            generation <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(segments[2]))
        {
            return false;
        }

        parts = new RefreshTokenParts(
            new AuthSessionId(sessionId),
            generation,
            segments[2]);

        return true;
    }

    public string HashSecret(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var bytes =
            Encoding.UTF8.GetBytes(secret);

        var hash =
            SHA256.HashData(bytes);

        return Convert.ToHexString(hash);
    }

    public bool VerifySecret(
        string secret,
        string expectedHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);

        byte[] expectedBytes;

        try
        {
            expectedBytes =
                Convert.FromHexString(expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualBytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(secret));

        return CryptographicOperations.FixedTimeEquals(
            actualBytes,
            expectedBytes);
    }
}
