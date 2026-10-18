using Microsoft.Extensions.Options;

namespace Displays.Application;

/// <summary>Displays tunables (<c>Displays</c> section).</summary>
public sealed class DisplaysOptions
{
    public const string SectionName = "Displays";

    /// <summary>How long a pairing code shown on a screen stays valid.</summary>
    public int PairingCodeMinutes { get; init; } = 10;

    /// <summary>A screen seen within this long counts as online.</summary>
    public int OnlineWindowSeconds { get; init; } = 90;

    /// <summary>Screens re-fetch their feed at least this often, even without a push.</summary>
    public int FeedRefreshSeconds { get; init; } = 30;

    /// <summary>Last-seen is written at most this often per screen.</summary>
    public int SeenWriteIntervalSeconds { get; init; } = 30;

    public int ReadyOrdersLimit { get; init; } = 30;

    /// <summary>Programme slots shown as "next" on schedule slides.</summary>
    public int ScheduleNextCount { get; init; } = 4;

    /// <summary>Most-requested songs shown on a DJ booth slide.</summary>
    public int DjQueueCount { get; init; } = 6;

    /// <summary>Where uploaded images are stored; relative paths are under the API's content root.</summary>
    public string AssetsRootPath { get; init; } = "App_Data/display-assets";

    public long MaxAssetBytes { get; init; } = 5 * 1024 * 1024;
}

public sealed class DisplaysOptionsValidator : IValidateOptions<DisplaysOptions>
{
    public ValidateOptionsResult Validate(string? name, DisplaysOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.PairingCodeMinutes is < 1 or > 60)
        {
            failures.Add("Displays:PairingCodeMinutes must be 1-60.");
        }

        if (options.OnlineWindowSeconds is < 15 or > 3600 || options.FeedRefreshSeconds is < 5 or > 600)
        {
            failures.Add("Displays:OnlineWindowSeconds must be 15-3600 and FeedRefreshSeconds 5-600.");
        }

        if (options.OnlineWindowSeconds <= options.FeedRefreshSeconds)
        {
            failures.Add("Displays:OnlineWindowSeconds must be longer than FeedRefreshSeconds, or polling screens flicker offline.");
        }

        if (options.SeenWriteIntervalSeconds is < 1 or > 600)
        {
            failures.Add("Displays:SeenWriteIntervalSeconds must be 1-600.");
        }

        if (options.ReadyOrdersLimit is < 1 or > 200 || options.ScheduleNextCount is < 1 or > 20)
        {
            failures.Add("Displays:ReadyOrdersLimit must be 1-200 and ScheduleNextCount 1-20.");
        }

        if (string.IsNullOrWhiteSpace(options.AssetsRootPath))
        {
            failures.Add("Displays:AssetsRootPath is required.");
        }

        if (options.MaxAssetBytes is < 10_000 or > 20 * 1024 * 1024)
        {
            failures.Add("Displays:MaxAssetBytes must be 10 KB-20 MB.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
