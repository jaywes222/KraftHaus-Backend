using System.Security.Cryptography;
using Displays.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Displays.Infrastructure.Authentication;

/// <summary>ASP.NET Core data protection for the few seconds a device token waits for its screen to pick it up.</summary>
internal sealed class PairingTokenProtector(IDataProtectionProvider provider) : IPairingTokenProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Displays.PairingToken.v1");

    public string Protect(string token) => _protector.Protect(token);

    public string? Unprotect(string protectedToken)
    {
        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
