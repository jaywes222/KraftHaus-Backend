using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Common.Domain.ResultMonad;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Music.Application;
using Music.Application.Catalog;
using Music.Domain.Common;

namespace Music.Infrastructure.Catalog;

/// <summary>
///     Song search on MusicBrainz (open data, every genre, free for commercial use). MusicBrainz asks for about one
///     request per second per caller and a meaningful User-Agent, so every call goes through <see cref="MusicBrainzGate" />,
///     searches and songs are cached, and a slow or failing MusicBrainz turns into <c>Music.CatalogUnavailable</c>
///     (the fan can still type the song in).
/// </summary>
internal sealed partial class MusicBrainzCatalog(
    HttpClient http,
    MusicBrainzGate gate,
    IMemoryCache cache,
    IOptions<MusicOptions> options,
    ILogger<MusicBrainzCatalog> logger)
    : IMusicCatalog
{
    private const string SearchPrefix = "music:mb:search:";
    private const string TrackPrefix = "music:mb:track:";

    private MusicOptions Settings => options.Value;

    public async Task<Result<IReadOnlyList<CatalogTrack>>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var key = SearchPrefix + query.ToUpperInvariant();
        if (cache.TryGetValue(key, out IReadOnlyList<CatalogTrack>? cached) && cached is not null)
        {
            return Result<IReadOnlyList<CatalogTrack>>.Success(cached);
        }

        // dismax: plain words are matched against title and artist, so "sauti sol suzanna" just works and
        // Lucene syntax in the text cannot change the query.
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"recording?query={Uri.EscapeDataString(query)}&dismax=true&limit={Settings.SearchResultLimit}&fmt=json");

        var response = await GetAsync<RecordingSearchResponse>(path, cancellationToken);
        if (response.IsFailure)
        {
            return response.Error!;
        }

        var tracks = (response.Value?.Recordings ?? [])
            .Select(Map)
            .OfType<CatalogTrack>()
            .DistinctBy(x => (x.Title.ToUpperInvariant(), x.Artist.ToUpperInvariant()))
            .ToList();

        cache.Set(key, (IReadOnlyList<CatalogTrack>)tracks, TimeSpan.FromMinutes(Settings.SearchCacheMinutes));

        // Requests come from a search result almost every time; seed the track cache so they need no extra call.
        foreach (var track in tracks)
        {
            cache.Set(TrackPrefix + track.Id, track, TimeSpan.FromHours(Settings.TrackCacheHours));
        }

        return Result<IReadOnlyList<CatalogTrack>>.Success(tracks);
    }

    public async Task<Result<CatalogTrack?>> GetAsync(string id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var recording))
        {
            return Result<CatalogTrack?>.Success(null);
        }

        var normalized = recording.ToString("D", CultureInfo.InvariantCulture);
        if (cache.TryGetValue(TrackPrefix + normalized, out CatalogTrack? cached) && cached is not null)
        {
            return cached;
        }

        var response = await GetAsync<Recording>(
            string.Create(CultureInfo.InvariantCulture, $"recording/{normalized}?inc=artist-credits+releases&fmt=json"),
            cancellationToken,
            notFoundIsEmpty: true);

        if (response.IsFailure)
        {
            return response.Error!;
        }

        var track = response.Value is { } r ? Map(r) : null;
        if (track is not null)
        {
            cache.Set(TrackPrefix + normalized, track, TimeSpan.FromHours(Settings.TrackCacheHours));
        }

        return Result<CatalogTrack?>.Success(track);
    }

    private async Task<Result<T?>> GetAsync<T>(string path, CancellationToken cancellationToken, bool notFoundIsEmpty = false)
        where T : class
    {
        using var turn = await gate.WaitAsync(cancellationToken);
        if (!turn.Granted)
        {
            LogBusy(logger);
            return MusicErrors.CatalogUnavailable;
        }

        try
        {
            using var response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);

            if (notFoundIsEmpty && response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result<T?>.Success(null);
            }

            if (!response.IsSuccessStatusCode)
            {
                // 503 is MusicBrainz's "slow down"; the gate keeps us under the limit, so this means they are busy.
                LogFailed(logger, (int)response.StatusCode);
                return MusicErrors.CatalogUnavailable;
            }

            return Result<T?>.Success(await response.Content.ReadFromJsonAsync<T>(cancellationToken));
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or JsonException) &&
                                   !cancellationToken.IsCancellationRequested)
        {
            LogError(logger, ex);
            return MusicErrors.CatalogUnavailable;
        }
    }

    private static CatalogTrack? Map(Recording recording)
    {
        if (!Guid.TryParse(recording.Id, out var id) || string.IsNullOrWhiteSpace(recording.Title))
        {
            return null;
        }

        var artist = ArtistName(recording.ArtistCredit);
        if (string.IsNullOrWhiteSpace(artist))
        {
            return null;
        }

        // Prefer an official album or single over compilations and bootlegs for the release shown.
        var release = (recording.Releases ?? [])
            .OrderBy(r => r.Status == "Official" ? 0 : 1)
            .ThenBy(r => r.ReleaseGroup?.PrimaryType is "Album" or "Single" or "EP" ? 0 : 1)
            .ThenBy(r => r.Date ?? "9999")
            .FirstOrDefault();

        return new CatalogTrack(
            id.ToString("D", CultureInfo.InvariantCulture),
            recording.Title.Trim(),
            artist,
            release?.Title?.Trim(),
            release?.Id,
            Year(recording.FirstReleaseDate),
            recording.Length is > 0 ? (int)(recording.Length.Value / 1000) : null);
    }

    /// <summary>"Sauti Sol" + " feat. " + "Nyashinski" → "Sauti Sol feat. Nyashinski".</summary>
    private static string ArtistName(IReadOnlyList<ArtistCredit>? credits)
    {
        if (credits is null || credits.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var credit in credits)
        {
            builder.Append(credit.Name ?? credit.Artist?.Name).Append(credit.JoinPhrase);
        }

        return builder.ToString().Trim();
    }

    private static int? Year(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;

    [LoggerMessage(EventId = 9701, Level = LogLevel.Warning, Message = "MusicBrainz search is busy; a request waited too long for its turn.")]
    private static partial void LogBusy(ILogger logger);

    [LoggerMessage(EventId = 9702, Level = LogLevel.Warning, Message = "MusicBrainz answered {StatusCode}.")]
    private static partial void LogFailed(ILogger logger, int statusCode);

    [LoggerMessage(EventId = 9703, Level = LogLevel.Warning, Message = "MusicBrainz call failed.")]
    private static partial void LogError(ILogger logger, Exception exception);

    // ---- Wire model (only the fields used) -----------------------------------------------------------------------

    internal sealed record RecordingSearchResponse(
        [property: JsonPropertyName("recordings")] IReadOnlyList<Recording>? Recordings);

    internal sealed record Recording(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("length")] long? Length,
        [property: JsonPropertyName("first-release-date")] string? FirstReleaseDate,
        [property: JsonPropertyName("artist-credit")] IReadOnlyList<ArtistCredit>? ArtistCredit,
        [property: JsonPropertyName("releases")] IReadOnlyList<Release>? Releases);

    internal sealed record ArtistCredit(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("joinphrase")] string? JoinPhrase,
        [property: JsonPropertyName("artist")] Artist? Artist);

    internal sealed record Artist([property: JsonPropertyName("name")] string? Name);

    internal sealed record Release(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("release-group")] ReleaseGroup? ReleaseGroup);

    internal sealed record ReleaseGroup([property: JsonPropertyName("primary-type")] string? PrimaryType);
}

/// <summary>
///     One MusicBrainz call at a time, at least <see cref="MusicOptions.MinIntervalMilliseconds" /> apart, for the
///     whole process (MusicBrainz limits per caller IP). A caller that would wait longer than
///     <see cref="MusicOptions.MaxQueueWaitMilliseconds" /> is turned away instead of queueing behind a crowd.
/// </summary>
internal sealed class MusicBrainzGate(TimeProvider time, IOptions<MusicOptions> options) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTimeOffset _nextAllowed = DateTimeOffset.MinValue;

    public async Task<Turn> WaitAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!await _lock.WaitAsync(settings.MaxQueueWaitMilliseconds, cancellationToken))
        {
            return new Turn(null);
        }

        var delay = _nextAllowed - time.GetUtcNow();
        if (delay > TimeSpan.Zero)
        {
            if (delay > TimeSpan.FromMilliseconds(settings.MaxQueueWaitMilliseconds))
            {
                _lock.Release();
                return new Turn(null);
            }

            try
            {
                await Task.Delay(delay, time, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _lock.Release();
                throw;
            }
        }

        return new Turn(this);
    }

    public void Dispose() => _lock.Dispose();

    private void Release()
    {
        _nextAllowed = time.GetUtcNow().AddMilliseconds(options.Value.MinIntervalMilliseconds);
        _lock.Release();
    }

    /// <summary>Holds the gate until disposed; the next caller may go one interval after that.</summary>
    internal readonly struct Turn(MusicBrainzGate? gate) : IDisposable
    {
        public bool Granted => gate is not null;

        public void Dispose() => gate?.Release();
    }
}
