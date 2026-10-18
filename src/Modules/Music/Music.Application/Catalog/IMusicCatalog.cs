using Common.Domain.ResultMonad;

namespace Music.Application.Catalog;

/// <summary>A song as the catalogue knows it.</summary>
/// <param name="Id">Catalogue id (MusicBrainz recording id).</param>
/// <param name="ReleaseId">Release the cover image comes from, when there is one.</param>
public sealed record CatalogTrack(
    string Id,
    string Title,
    string Artist,
    string? Release,
    string? ReleaseId,
    int? Year,
    int? DurationSeconds);

/// <summary>
///     The global song catalogue behind the request search (MusicBrainz). Implementations must cope with the
///     provider being slow or down: they return <c>Music.CatalogUnavailable</c> instead of throwing.
/// </summary>
public interface IMusicCatalog
{
    Task<Result<IReadOnlyList<CatalogTrack>>> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>One song by catalogue id; <c>null</c> when the catalogue has no such song.</summary>
    Task<Result<CatalogTrack?>> GetAsync(string id, CancellationToken cancellationToken);
}
