using System.Security.Cryptography;
using System.Text;
using Common.Application.Auditing;
using Identity.Application.Auditing;

namespace Identity.Tests.Auditing;

public sealed class RecordingIdentityAuditTrail : IIdentityAuditTrail
{
    private readonly List<IdentityAuditRejection> _rejections = [];

    public IReadOnlyList<IdentityAuditRejection> Rejections => _rejections;

    public Task RecordRejectedAsync(
        IdentityAuditRejection rejection,
        CancellationToken cancellationToken)
    {
        _rejections.Add(rejection);

        return Task.CompletedTask;
    }
}

/// <summary>Deterministic fingerprint so tests can assert the raw value never leaks.</summary>
public sealed class FakeFingerprinter : IAuditFingerprinter
{
    public string Fingerprint(string value) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes("test:" + (value ?? string.Empty).Trim().ToUpperInvariant())));
}
