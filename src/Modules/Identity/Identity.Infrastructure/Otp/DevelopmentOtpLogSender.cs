using Identity.Application.Abstractions;
using Identity.Domain.Otp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Otp;

/// <summary>
///     <c>Identity:Development</c>. Local-development conveniences that must never be on anywhere else.
/// </summary>
public sealed class IdentityDevelopmentOptions
{
    public const string SectionName = "Identity:Development";

    /// <summary>
    ///     Writes every OTP code to the application log so sign-in can be tested from Postman without a real SMS or
    ///     e-mail provider. Startup fails if this is enabled outside the Development environment.
    /// </summary>
    public bool LogOtpCodes { get; init; }

    /// <summary>
    ///     Development only: a Tenant Admin seeded for this host's tenant so staff management can be tried at once.
    ///     Sign in to the host with <see cref="AdminEmail" /> (e-mail OTP) or <see cref="AdminPhone" />.
    /// </summary>
    public string AdminHost { get; init; } = "localhost";

    public string? AdminEmail { get; init; }

    public string? AdminPhone { get; init; }

    public string AdminDisplayName { get; init; } = "Development Admin";
}

public sealed class IdentityDevelopmentOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<IdentityDevelopmentOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityDevelopmentOptions options) =>
        options.LogOtpCodes && !environment.IsDevelopment()
            ? ValidateOptionsResult.Fail(
                $"{IdentityDevelopmentOptions.SectionName}:{nameof(IdentityDevelopmentOptions.LogOtpCodes)} " +
                $"is only allowed in Development (current environment: {environment.EnvironmentName}).")
            : ValidateOptionsResult.Success;
}

/// <summary>
///     Decorates the real <see cref="IOtpSender" />: the message is still handed to the configured SMS / e-mail
///     gateway, and in Development (with <see cref="IdentityDevelopmentOptions.LogOtpCodes" />) the code is also
///     logged. Outside Development it is a pass-through even if the option were somehow set.
/// </summary>
internal sealed partial class DevelopmentOtpLogSender(
    IOtpSender inner,
    IOptions<IdentityDevelopmentOptions> options,
    IHostEnvironment environment,
    ILogger<DevelopmentOtpLogSender> logger)
    : IOtpSender
{
    public async Task SendAsync(
        OtpChannel channel,
        string destination,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        await inner.SendAsync(channel, destination, code, purpose, cancellationToken);

        if (options.Value.LogOtpCodes && environment.IsDevelopment())
        {
            LogDevelopmentOtp(logger, code, purpose, channel, destination);
        }
    }

    [LoggerMessage(
        EventId = 9001,
        Level = LogLevel.Warning,
        Message = "DEVELOPMENT ONLY: OTP {Code} ({Purpose}) sent by {Channel} to {Destination}")]
    private static partial void LogDevelopmentOtp(
        ILogger logger,
        string code,
        OtpPurpose purpose,
        OtpChannel channel,
        string destination);
}
