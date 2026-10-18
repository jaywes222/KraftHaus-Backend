using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Payments.Infrastructure.PalPluss;

public enum PalPlussMode
{
    /// <summary>https://api.palpluss.com/v1 with a live key (KYC-approved account).</summary>
    Live = 1,

    /// <summary>https://sandbox.palpluss.com/v1.</summary>
    Sandbox = 2,

    /// <summary>
    ///     No network: an in-process fake that "succeeds" after a delay. Development only. Phones ending 1032 cancel,
    ///     ending 0001 fail, so every result screen can be tried.
    /// </summary>
    Simulated = 3
}

/// <summary>PalPluss credentials and client settings (<c>Payments:PalPluss</c>). The API key is a secret: user-secrets or environment only.</summary>
public sealed class PalPlussOptions
{
    public const string SectionName = "Payments:PalPluss";

    public PalPlussMode Mode { get; init; } = PalPlussMode.Simulated;

    public string? ApiKey { get; init; }

    /// <summary>Payment channel (till/paybill) for ticket money. Empty: the account's default channel.</summary>
    public Guid? ChannelId { get; init; }

    /// <summary>Overrides the mode's base URL (must end with <c>/v1/</c>).</summary>
    public Uri? BaseUrl { get; init; }

    public int TimeoutSeconds { get; init; } = 15;

    /// <summary>Simulated mode: how long a prompt takes to "complete".</summary>
    public int SimulatedCompletionSeconds { get; init; } = 10;

    public Uri ResolveBaseUrl() =>
        BaseUrl ?? Mode switch
        {
            PalPlussMode.Live => new Uri("https://api.palpluss.com/v1/"),
            _ => new Uri("https://sandbox.palpluss.com/v1/")
        };
}

public sealed class PalPlussOptionsValidator(IHostEnvironment environment) : IValidateOptions<PalPlussOptions>
{
    public ValidateOptionsResult Validate(string? name, PalPlussOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!Enum.IsDefined(options.Mode))
        {
            failures.Add("Payments:PalPluss:Mode must be Live, Sandbox or Simulated.");
        }

        if (options.Mode == PalPlussMode.Simulated && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            failures.Add("Payments:PalPluss:Mode Simulated is only allowed in Development.");
        }

        if (options.Mode is PalPlussMode.Live or PalPlussMode.Sandbox && string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Payments:PalPluss:ApiKey is required outside Simulated mode (user-secrets or environment).");
        }

        if (options.BaseUrl is { } url && (url.Scheme != Uri.UriSchemeHttps || !url.AbsolutePath.EndsWith('/')))
        {
            failures.Add("Payments:PalPluss:BaseUrl must be https and end with '/'.");
        }

        if (options.TimeoutSeconds is < 3 or > 60 || options.SimulatedCompletionSeconds is < 1 or > 300)
        {
            failures.Add("Payments:PalPluss:TimeoutSeconds must be 3-60 and SimulatedCompletionSeconds 1-300.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
