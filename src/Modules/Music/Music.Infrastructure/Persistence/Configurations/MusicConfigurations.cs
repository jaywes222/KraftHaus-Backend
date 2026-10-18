using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Music.Domain.Common;
using Music.Domain.Requests;

namespace Music.Infrastructure.Persistence.Configurations;

public sealed class RequestBoardConfiguration : IEntityTypeConfiguration<RequestBoard>
{
    public void Configure(EntityTypeBuilder<RequestBoard> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("request_board");
        builder.Ignore(x => x.Events);
        MusicEf.ConfigureKey<RequestBoard, RequestBoardId>(builder);

        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(RequestBoard.NoteMaxLength);

        // One board per event.
        builder.HasIndex(x => x.EventId).IsUnique();

        MusicEf.ConfigureAudit(builder);
        MusicEf.ConfigureAggregateVersion(builder);
    }
}

public sealed class SongRequestConfiguration : IEntityTypeConfiguration<SongRequest>
{
    /// <summary>Partial unique index: one open request per song per event (a second fan adds a vote instead).</summary>
    public const string OpenRequestIndex = "ux_song_request_open_track";

    public void Configure(EntityTypeBuilder<SongRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("song_request");
        builder.Ignore(x => x.Events);
        builder.Ignore(x => x.IsOpen);
        MusicEf.ConfigureKey<SongRequest, SongRequestId>(builder);

        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.TrackKey).HasMaxLength(Track.KeyMaxLength).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.OwnsOne(x => x.Track, track =>
        {
            track.Property(x => x.Source).HasColumnName("TrackSource").HasConversion<string>().HasMaxLength(16).IsRequired();
            track.Property(x => x.ExternalId).HasColumnName("TrackExternalId").HasMaxLength(Track.ExternalIdLength);
            track.Property(x => x.Title).HasColumnName("TrackTitle").HasMaxLength(Track.TitleMaxLength).IsRequired();
            track.Property(x => x.Artist).HasColumnName("TrackArtist").HasMaxLength(Track.ArtistMaxLength).IsRequired();
            track.Property(x => x.Release).HasColumnName("TrackRelease").HasMaxLength(Track.ReleaseMaxLength);
            track.Property(x => x.ReleaseId).HasColumnName("TrackReleaseId").HasMaxLength(Track.ExternalIdLength);
            track.Property(x => x.Year).HasColumnName("TrackYear");
            track.Property(x => x.DurationSeconds).HasColumnName("TrackDurationSeconds");
            // Stored once, as the request's TrackKey column (it drives the unique index); only new tracks need it.
            track.Ignore(x => x.Key);
        });
        builder.Navigation(x => x.Track).IsRequired();

        builder.HasMany(x => x.Votes).WithOne().HasForeignKey("SongRequestId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Votes).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasIndex(x => new { x.EventId, x.TrackKey })
            .HasDatabaseName(OpenRequestIndex)
            .IsUnique()
            .HasFilter("\"Status\" IN ('Waiting', 'UpNext')");
        builder.HasIndex(x => new { x.EventId, x.Status, x.LastVotedAt });

        MusicEf.ConfigureAudit(builder);
        MusicEf.ConfigureAggregateVersion(builder);
    }
}

public sealed class SongRequestVoteConfiguration : IEntityTypeConfiguration<SongRequestVote>
{
    public void Configure(EntityTypeBuilder<SongRequestVote> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("song_request_vote");
        MusicEf.ConfigureKey<SongRequestVote, SongRequestVoteId>(builder);

        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(SongRequestVote.NameMaxLength).IsRequired();
        builder.Property(x => x.VotedAt).IsRequired();

        // One vote per person per request; counting a person's requests for an event goes through the request.
        builder.HasIndex("SongRequestId", nameof(SongRequestVote.UserId)).IsUnique();
        builder.HasIndex(x => x.UserId);

        MusicEf.ConfigureAudit(builder);
    }
}
