using Common.Domain.ResultMonad;
using Music.Application.Catalog;
using Music.Domain.Common;

namespace Api.IntegrationTests.Music;

/// <summary>MusicBrainz stand-in: two known songs; "offline" makes every call report the catalogue unavailable.</summary>
public sealed class FakeMusicCatalog : IMusicCatalog
{
    public static readonly CatalogTrack Suzanna = new(
        "6a1d6a4c-8c27-4f5e-9b5c-2f2f2b1a0c11",
        "Suzanna",
        "Sauti Sol",
        "Live and Die in Afrika",
        "0f5a8a8e-1d2c-4c6b-9a51-6f0f0e3b7d22",
        2015,
        228);

    public static readonly CatalogTrack Unavailable = new(
        "2b7f1c9e-4d3a-4e8b-8f6a-1c2d3e4f5a66",
        "Unavailable",
        "Davido",
        "Timeless",
        null,
        2023,
        172);

    private static readonly CatalogTrack[] Songs = [Suzanna, Unavailable];

    public bool Offline { get; set; }

    public Task<Result<IReadOnlyList<CatalogTrack>>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (Offline)
        {
            return Task.FromResult<Result<IReadOnlyList<CatalogTrack>>>(MusicErrors.CatalogUnavailable);
        }

        IReadOnlyList<CatalogTrack> found = [.. Songs.Where(s =>
            s.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            s.Artist.Contains(query, StringComparison.OrdinalIgnoreCase))];

        return Task.FromResult(Result<IReadOnlyList<CatalogTrack>>.Success(found));
    }

    public Task<Result<CatalogTrack?>> GetAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(Result<CatalogTrack?>.Success(Songs.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))));
}
