using Common.Application.Auditing;
using Xunit;

namespace Common.Tests.Auditing;

public sealed class AuditMetadataSanitizerTests
{
    [Theory]
    [InlineData("eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiIxMjMifQ.c2lnbmF0dXJl")]
    [InlineData("0199c3a1-6f2e-7a10-9b55-0e4f3b2a1c00.3.Q1w2E3r4T5y6U7i8O9p0AsDfGhJkL")]
    [InlineData("user@example.com")]
    [InlineData("+254712345678")]
    [InlineData("0712 345 678")]
    [InlineData("123456")]
    [InlineData("Bearer abc.def")]
    [InlineData("5F2B9C0D7A1E4B3C8D6F0A2B4C6D8E0F1A3B5C7D")]
    public void Sanitize_ValueThatLooksLikeSecretOrPersonalData_IsRedactedWhateverTheKey(string value)
    {
        var sanitized = AuditMetadataSanitizer.Sanitize(
            new Dictionary<string, string> { ["note"] = value });

        Assert.Equal(AuditMetadataSanitizer.Redacted, sanitized["note"]);
    }

    [Theory]
    [InlineData("otp")]
    [InlineData("otpCode")]
    [InlineData("code")]
    [InlineData("refreshToken")]
    [InlineData("accessToken")]
    [InlineData("secret")]
    [InlineData("codeHash")]
    [InlineData("phone")]
    [InlineData("email")]
    [InlineData("destination")]
    [InlineData("displayName")]
    [InlineData("ipAddress")]
    [InlineData("authorization")]
    public void Sanitize_SensitiveKey_IsRedactedEvenWhenValueLooksHarmless(string key)
    {
        var sanitized = AuditMetadataSanitizer.Sanitize(
            new Dictionary<string, string> { [key] = "harmless" });

        Assert.Equal(AuditMetadataSanitizer.Redacted, sanitized[key]);
    }

    [Theory]
    [InlineData("channel", "Phone")]
    [InlineData("purpose", "SignIn")]
    [InlineData("attemptCount", "3")]
    [InlineData("policyVersion", "3")]
    [InlineData("role", "TenantAdmin")]
    [InlineData("scopeId", "0199c3a1-6f2e-7a10-9b55-0e4f3b2a1c00")]
    [InlineData("permission", "users:view")]
    [InlineData("revocationReason", "ReuseDetected")]
    public void Sanitize_SafeContext_IsKept(string key, string value)
    {
        var sanitized = AuditMetadataSanitizer.Sanitize(
            new Dictionary<string, string> { [key] = value });

        Assert.Equal(value, sanitized[key]);
    }

    [Fact]
    public void Sanitize_FingerprintKeyWithHmacValue_IsKept()
    {
        var fingerprint = new string('A', 64);

        var sanitized = AuditMetadataSanitizer.Sanitize(
            new Dictionary<string, string> { ["destinationFingerprint"] = fingerprint });

        Assert.Equal(fingerprint, sanitized["destinationFingerprint"]);
    }

    [Fact]
    public void Sanitize_FingerprintKeyWithRawValue_IsRedacted()
    {
        var sanitized = AuditMetadataSanitizer.Sanitize(
            new Dictionary<string, string> { ["destinationFingerprint"] = "+254712345678" });

        Assert.Equal(AuditMetadataSanitizer.Redacted, sanitized["destinationFingerprint"]);
    }

    [Fact]
    public void Sanitize_InvalidKeys_AreDropped()
    {
        var sanitized = AuditMetadataSanitizer.Sanitize(
            new Dictionary<string, string>
            {
                ["has space"] = "x",
                ["1leadingDigit"] = "x",
                ["$where"] = "x",
                ["ok"] = "x"
            });

        Assert.Equal(["ok"], sanitized.Keys);
    }

    [Fact]
    public void Sanitize_CapsEntryCountAndValueLength()
    {
        var metadata = Enumerable
            .Range(0, AuditMetadataSanitizer.MaxEntries + 5)
            .ToDictionary(i => $"k{i:D2}", _ => new string('x', 10) + " " + new string('y', 300));

        var sanitized = AuditMetadataSanitizer.Sanitize(metadata);

        Assert.Equal(AuditMetadataSanitizer.MaxEntries, sanitized.Count);
        Assert.All(sanitized.Values, v => Assert.True(v.Length <= AuditMetadataSanitizer.MaxValueLength));
    }

    [Fact]
    public void Sanitize_Null_ReturnsEmpty()
    {
        Assert.Empty(AuditMetadataSanitizer.Sanitize(null));
    }
}
