using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Music.Domain.Requests;

namespace Music.Application;

/// <summary><c>Music</c>: the song catalogue and request defaults. Validated at start-up.</summary>
public sealed class MusicOptions
{
    public const string SectionName = "Music";

    /// <summary>MusicBrainz web service root (must end with <c>/ws/2/</c>).</summary>
    public Uri MusicBrainzBaseUrl { get; init; } = new("https://musicbrainz.org/ws/2/");

    /// <summary>
    ///     MusicBrainz requires a meaningful User-Agent with a way to reach the operator, or it blocks the caller:
    ///     "KRAFTHAUS/1.0 ( ops@example.com )". Required outside Development.
    /// </summary>
    public string UserAgent { get; init; } = string.Empty;

    /// <summary>MusicBrainz allows about one request per second per caller; calls are spaced at least this far apart.</summary>
    public int MinIntervalMilliseconds { get; init; } = 1100;

    /// <summary>A search waiting longer than this for its turn gives up with "search is busy" (the fan can type the song).</summary>
    public int MaxQueueWaitMilliseconds { get; init; } = 4000;

    public int TimeoutSeconds { get; init; } = 6;

    public int SearchResultLimit { get; init; } = 10;

    public int SearchCacheMinutes { get; init; } = 30;

    public int TrackCacheHours { get; init; } = 24;

    /// <summary>Requests per person per event when the DJ console first opens an event's board.</summary>
    public int DefaultMaxPerPerson { get; init; } = 3;

    /// <summary>
    ///     Requests open as soon as the event is published, so ticket holders can ask before the night as well as
    ///     during it. The DJ can still close (and reopen) the board from the console.
    /// </summary>
    public bool OpenByDefault { get; init; } = true;

    /// <summary>Requests stop on their own this many minutes after the event's last date ends.</summary>
    public int CloseAfterEventMinutes { get; init; }

    /// <summary>Search text shorter than this is not sent to the catalogue.</summary>
    public int MinQueryLength { get; init; } = 2;
}

public sealed class MusicOptionsValidator(IHostEnvironment environment) : IValidateOptions<MusicOptions>
{
    public ValidateOptionsResult Validate(string? name, MusicOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        // Plain http only for a local stand-in during development.
        var local = environment.IsEnvironment("Development") || environment.IsEnvironment("Testing");
        if (options.MusicBrainzBaseUrl is not { IsAbsoluteUri: true } url ||
            (url.Scheme != Uri.UriSchemeHttps && !(local && url.Scheme == Uri.UriSchemeHttp)) ||
            !url.AbsolutePath.EndsWith('/'))
        {
            failures.Add("Music:MusicBrainzBaseUrl must be an https URL ending with '/'.");
        }

        if (string.IsNullOrWhiteSpace(options.UserAgent) && !local)
        {
            failures.Add("Music:UserAgent is required (MusicBrainz blocks anonymous callers), e.g. \"KRAFTHAUS/1.0 ( ops@krafthaus.co.ke )\".");
        }

        if (options.MinIntervalMilliseconds is < 1000 or > 10_000)
        {
            failures.Add("Music:MinIntervalMilliseconds must be 1000-10000 (MusicBrainz allows one request a second).");
        }

        if (options.MaxQueueWaitMilliseconds is < 0 or > 30_000 ||
            options.TimeoutSeconds is < 2 or > 30 ||
            options.SearchResultLimit is < 1 or > 25 ||
            options.SearchCacheMinutes is < 1 or > 1440 ||
            options.TrackCacheHours is < 1 or > 720 ||
            options.MinQueryLength is < 1 or > 10 ||
            options.CloseAfterEventMinutes is < 0 or > 1440)
        {
            failures.Add("Music: MaxQueueWaitMilliseconds 0-30000, TimeoutSeconds 2-30, SearchResultLimit 1-25, SearchCacheMinutes 1-1440, TrackCacheHours 1-720, MinQueryLength 1-10, CloseAfterEventMinutes 0-1440.");
        }

        if (options.DefaultMaxPerPerson is < RequestBoard.MinPerPerson or > RequestBoard.MaxPerPersonLimit)
        {
            failures.Add($"Music:DefaultMaxPerPerson must be {RequestBoard.MinPerPerson}-{RequestBoard.MaxPerPersonLimit}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
