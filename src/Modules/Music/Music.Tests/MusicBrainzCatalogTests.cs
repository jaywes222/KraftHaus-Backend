using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Music.Application;
using Music.Infrastructure.Catalog;
using Xunit;

namespace Music.Tests;

public sealed class MusicBrainzCatalogTests
{
    private const string RecordingId = "6a1d6a4c-8c27-4f5e-9b5c-2f2f2b1a0c11";
    private const string ReleaseId = "0f5a8a8e-1d2c-4c6b-9a51-6f0f0e3b7d22";

    // Trimmed from a real /ws/2/recording?query=…&fmt=json answer: only the fields the catalogue reads.
    private const string SearchJson = $$"""
        {
          "count": 2,
          "recordings": [
            {
              "id": "{{RecordingId}}",
              "score": 100,
              "title": "Suzanna",
              "length": 228000,
              "first-release-date": "2016-01-29",
              "artist-credit": [ { "name": "Sauti Sol", "joinphrase": " feat. ", "artist": { "name": "Sauti Sol" } },
                                 { "name": "Nyashinski", "artist": { "name": "Nyashinski" } } ],
              "releases": [
                { "id": "11111111-1111-1111-1111-111111111111", "title": "Afro Hits 2016", "status": "Official", "date": "2016-05-01",
                  "release-group": { "primary-type": "Compilation" } },
                { "id": "{{ReleaseId}}", "title": "Live and Die in Afrika", "status": "Official", "date": "2015-12-01",
                  "release-group": { "primary-type": "Album" } }
              ]
            },
            { "id": "not-a-guid", "title": "Broken", "artist-credit": [ { "name": "Nobody" } ] }
          ]
        }
        """;

    [Fact]
    public async Task SearchReadsTheCatalogueAndCachesTheAnswer()
    {
        var calls = new List<HttpRequestMessage>();
        using var handler = new Handler(request =>
        {
            calls.Add(request);
            return Json(SearchJson);
        });

        var catalog = Catalog(handler, out _);

        var first = await catalog.SearchAsync("sauti sol suzanna", CancellationToken.None);
        var track = Assert.Single(first.Value!);

        Assert.Equal(RecordingId, track.Id);
        Assert.Equal("Suzanna", track.Title);
        Assert.Equal("Sauti Sol feat. Nyashinski", track.Artist);
        Assert.Equal("Live and Die in Afrika", track.Release);
        Assert.Equal(ReleaseId, track.ReleaseId);
        Assert.Equal(2016, track.Year);
        Assert.Equal(228, track.DurationSeconds);

        var uri = Assert.Single(calls).RequestUri!.AbsoluteUri;
        Assert.StartsWith("https://musicbrainz.org/ws/2/recording?query=sauti%20sol%20suzanna&dismax=true&limit=10&fmt=json", uri, StringComparison.Ordinal);

        // Same search again, and the song picked from it: no further calls.
        Assert.Single((await catalog.SearchAsync("SAUTI SOL SUZANNA", CancellationToken.None)).Value!);
        Assert.Equal("Suzanna", (await catalog.GetAsync(RecordingId.ToUpperInvariant(), CancellationToken.None)).Value!.Title);
        Assert.Single(calls);
    }

    [Fact]
    public async Task AnUnknownSongIsEmptyAndABusyCatalogueIsUnavailable()
    {
        using var handler = new Handler(request =>
            request.RequestUri!.AbsolutePath.Contains("recording/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var catalog = Catalog(handler, out _, minInterval: 1000);

        var unknown = await catalog.GetAsync(Guid.NewGuid().ToString(), CancellationToken.None);
        Assert.False(unknown.IsFailure);
        Assert.Null(unknown.Value);

        var busy = await catalog.SearchAsync("anything", CancellationToken.None);
        Assert.Equal("Music.CatalogUnavailable", busy.Error!.Key);
    }

    [Fact]
    public async Task ACallerThatWouldWaitTooLongIsTurnedAway()
    {
        using var handler = new Handler(_ => Json("""{"recordings":[]}"""));
        var catalog = Catalog(handler, out _, minInterval: 5000, maxWait: 100);

        Assert.False((await catalog.SearchAsync("first", CancellationToken.None)).IsFailure);

        // The next call may only go five seconds later; waiting that long is not allowed.
        var second = await catalog.SearchAsync("second", CancellationToken.None);
        Assert.Equal("Music.CatalogUnavailable", second.Error!.Key);
    }

    [Fact]
    public async Task ATimeoutIsUnavailableNotAnError()
    {
        using var handler = new Handler(_ => throw new TaskCanceledException("timeout"));
        var catalog = Catalog(handler, out _);

        Assert.Equal("Music.CatalogUnavailable", (await catalog.SearchAsync("slow", CancellationToken.None)).Error!.Key);
    }

    private static MusicBrainzCatalog Catalog(Handler handler, out MemoryCache cache, int minInterval = 1000, int maxWait = 4000)
    {
        var options = Options.Create(new MusicOptions { MinIntervalMilliseconds = minInterval, MaxQueueWaitMilliseconds = maxWait });
        cache = new MemoryCache(new MemoryCacheOptions());

#pragma warning disable CA2000 // Owned by the catalogue under test for the length of the test.
        var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = options.Value.MusicBrainzBaseUrl };
        var gate = new MusicBrainzGate(TimeProvider.System, options);
#pragma warning restore CA2000

        return new MusicBrainzCatalog(http, gate, cache, options, NullLogger<MusicBrainzCatalog>.Instance);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
