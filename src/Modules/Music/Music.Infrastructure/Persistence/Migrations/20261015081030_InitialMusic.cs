using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Music.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMusic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "music");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "music",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Module = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ActorType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActorSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TargetId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "request_board",
                schema: "music",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false),
                    MaxPerPerson = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_board", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "song_request",
                schema: "music",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackSource = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TrackExternalId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    TrackTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TrackArtist = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TrackRelease = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TrackReleaseId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    TrackYear = table.Column<int>(type: "integer", nullable: true),
                    TrackDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    TrackKey = table.Column<string>(type: "character varying(420)", maxLength: 420, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastVotedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VoteCount = table.Column<int>(type: "integer", nullable: false),
                    HandledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HandledBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_song_request", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "song_request_vote",
                schema: "music",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    VotedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SongRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_song_request_vote", x => x.Id);
                    table.ForeignKey(
                        name: "FK_song_request_vote_song_request_SongRequestId",
                        column: x => x.SongRequestId,
                        principalSchema: "music",
                        principalTable: "song_request",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_ActorId_OccurredAt",
                schema: "music",
                table: "audit_log",
                columns: new[] { "ActorId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_OccurredAt",
                schema: "music",
                table: "audit_log",
                column: "OccurredAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_TargetType_TargetId_OccurredAt",
                schema: "music",
                table: "audit_log",
                columns: new[] { "TargetType", "TargetId", "OccurredAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_TraceId",
                schema: "music",
                table: "audit_log",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_request_board_EventId",
                schema: "music",
                table: "request_board",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_song_request_EventId_Status_LastVotedAt",
                schema: "music",
                table: "song_request",
                columns: new[] { "EventId", "Status", "LastVotedAt" });

            migrationBuilder.CreateIndex(
                name: "ux_song_request_open_track",
                schema: "music",
                table: "song_request",
                columns: new[] { "EventId", "TrackKey" },
                unique: true,
                filter: "\"Status\" IN ('Waiting', 'UpNext')");

            migrationBuilder.CreateIndex(
                name: "IX_song_request_vote_SongRequestId_UserId",
                schema: "music",
                table: "song_request_vote",
                columns: new[] { "SongRequestId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_song_request_vote_UserId",
                schema: "music",
                table: "song_request_vote",
                column: "UserId");

            // Append-only at the database level (Spec 08 §13), same rule as events.audit_log and displays.audit_log: only a retention purge
            // that sets krafthaus.audit_purge = 'on' for its own transaction may delete rows.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION music.audit_log_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND current_setting('krafthaus.audit_purge', true) = 'on' THEN
                        RETURN OLD;
                    END IF;

                    RAISE EXCEPTION 'music.audit_log is append-only: % is not allowed', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_audit_log_append_only
                    BEFORE UPDATE OR DELETE ON music.audit_log
                    FOR EACH ROW EXECUTE FUNCTION music.audit_log_append_only();

                CREATE TRIGGER trg_audit_log_no_truncate
                    BEFORE TRUNCATE ON music.audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION music.audit_log_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_audit_log_no_truncate ON music.audit_log;
                DROP TRIGGER IF EXISTS trg_audit_log_append_only ON music.audit_log;
                DROP FUNCTION IF EXISTS music.audit_log_append_only();
                """);

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "music");

            migrationBuilder.DropTable(
                name: "request_board",
                schema: "music");

            migrationBuilder.DropTable(
                name: "song_request_vote",
                schema: "music");

            migrationBuilder.DropTable(
                name: "song_request",
                schema: "music");
        }
    }
}
