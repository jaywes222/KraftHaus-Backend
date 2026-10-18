using System.Globalization;
using System.Text;
using Common.Domain.ResultMonad;
using Music.Domain.Common;

namespace Music.Domain.Requests;

/// <summary>Where a requested song's details came from.</summary>
public enum TrackSource
{
    /// <summary>Picked from the MusicBrainz catalogue (the details were looked up by the server).</summary>
    MusicBrainz = 1,

    /// <summary>Typed by the customer because the song wasn't in the catalogue (local releases often aren't).</summary>
    Typed = 2
}

/// <summary>
///     The song a request is for. <see cref="Key" /> identifies "the same song" so that a second fan's request adds
///     a vote instead of a duplicate row: the MusicBrainz recording id, or the typed title and artist folded to a
///     plain form.
/// </summary>
public sealed record Track
{
    public const int TitleMaxLength = 200;
    public const int ArtistMaxLength = 200;
    public const int ReleaseMaxLength = 200;
    public const int ExternalIdLength = 36;
    public const int KeyMaxLength = 420;
    public const int TypedMinLength = 2;
    public const int TypedTitleMaxLength = 100;
    public const int TypedArtistMaxLength = 100;

    private Track()
    {
    }

    public TrackSource Source { get; private init; }

    /// <summary>MusicBrainz recording id; <c>null</c> for typed songs.</summary>
    public string? ExternalId { get; private init; }

    public string Title { get; private init; } = string.Empty;

    public string Artist { get; private init; } = string.Empty;

    public string? Release { get; private init; }

    /// <summary>MusicBrainz release id, for the cover image (Cover Art Archive).</summary>
    public string? ReleaseId { get; private init; }

    public int? Year { get; private init; }

    public int? DurationSeconds { get; private init; }

    public string Key { get; private init; } = string.Empty;

    /// <summary>A catalogue song. The server looked these details up itself; they are only trimmed and clipped.</summary>
    public static Result<Track> FromCatalog(
        string externalId,
        string title,
        string artist,
        string? release,
        string? releaseId,
        int? year,
        int? durationSeconds)
    {
        if (!Guid.TryParse(externalId, out var recording) ||
            string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(artist))
        {
            return MusicErrors.TrackNotFound;
        }

        var id = recording.ToString("D", CultureInfo.InvariantCulture);

        return new Track
        {
            Source = TrackSource.MusicBrainz,
            ExternalId = id,
            Title = Clip(title, TitleMaxLength)!,
            Artist = Clip(artist, ArtistMaxLength)!,
            Release = Clip(release, ReleaseMaxLength),
            ReleaseId = Guid.TryParse(releaseId, out var r) ? r.ToString("D", CultureInfo.InvariantCulture) : null,
            Year = year is >= 1900 and <= 2100 ? year : null,
            DurationSeconds = durationSeconds is > 0 and < 7200 ? durationSeconds : null,
            Key = $"mb:{id}"
        };
    }

    /// <summary>A song the customer typed: validated like any other input.</summary>
    public static Result<Track> Typed(string? title, string? artist)
    {
        var t = Collapse(title);
        var a = Collapse(artist);
        var errors = new List<string>();

        if (t.Length is < TypedMinLength or > TypedTitleMaxLength)
        {
            errors.Add($"Title: Use {TypedMinLength}-{TypedTitleMaxLength} characters.");
        }

        if (a.Length is < TypedMinLength or > TypedArtistMaxLength)
        {
            errors.Add($"Artist: Use {TypedMinLength}-{TypedArtistMaxLength} characters.");
        }

        if (errors.Count > 0)
        {
            return MusicErrors.Invalid("RequestInvalid", errors);
        }

        return new Track
        {
            Source = TrackSource.Typed,
            Title = t,
            Artist = a,
            Key = Clip($"typed:{Fold(t)}|{Fold(a)}", KeyMaxLength)!
        };
    }

    private static string Collapse(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? Clip(string? value, int max)
    {
        var v = Collapse(value);
        if (v.Length == 0)
        {
            return null;
        }

        return v.Length <= max ? v : v[..max];
    }

    /// <summary>"Beyoncé  -  Halo!" → "beyonce halo": case, accents, punctuation and spacing don't make a new song.</summary>
    private static string Fold(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var space = false;

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (!space && builder.Length > 0)
            {
                builder.Append(' ');
                space = true;
            }
        }

        return builder.ToString().TrimEnd();
    }
}
