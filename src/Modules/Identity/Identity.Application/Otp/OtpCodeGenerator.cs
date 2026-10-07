using System.Globalization;
using System.Security.Cryptography;
using Identity.Application.Abstractions;

namespace Identity.Application.Otp;

public sealed class OtpCodeGenerator : IOtpCodeGenerator
{
    public string Generate()
    {
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);

        return value.ToString(
            "D6",
            CultureInfo.InvariantCulture);
    }
}
