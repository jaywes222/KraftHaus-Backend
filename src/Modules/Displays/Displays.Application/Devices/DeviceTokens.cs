using System.Security.Cryptography;
using System.Text;

namespace Displays.Application.Devices;

/// <summary>Device tokens: <c>khd_</c> + 43 url-safe characters (256 random bits). Only their SHA-256 is stored.</summary>
public static class DeviceTokens
{
    public const string Prefix = "khd_";

    public static string New() => Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string NewSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static bool LooksLikeToken(string? value) =>
        value is { Length: > 40 and < 100 } && value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Constant-time comparison of a presented secret with a stored hash.</summary>
    public static bool Matches(string? presented, string storedHash) =>
        !string.IsNullOrEmpty(presented) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(presented)), Encoding.ASCII.GetBytes(storedHash));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
