using System.Security.Cryptography;
using System.Text;

namespace Identity.Application.Sessions;

public sealed class RefreshTokenService : IRefreshTokenService
{
    public string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public string Hash(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var bytes = Encoding.UTF8.GetBytes(refreshToken);

        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash);
    }

    public bool Verify(
        string refreshToken,
        string expectedHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);

        byte[] expectedBytes;

        try
        {
            expectedBytes = Convert.FromHexString(expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualBytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(refreshToken));

        return CryptographicOperations.FixedTimeEquals(
            actualBytes,
            expectedBytes);
    }
}
