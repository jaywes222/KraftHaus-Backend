using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Displays.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDisplays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "displays");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "displays",
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
                name: "display_asset",
                schema: "displays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_display_asset", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "display_configuration",
                schema: "displays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Slides = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_display_configuration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "display_device",
                schema: "displays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Location = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultConfigurationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PairedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PairedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokeReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Schedule = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_display_device", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "display_pairing",
                schema: "displays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingToken = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_display_pairing", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "programme_slot",
                schema: "displays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventDateId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Who = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_programme_slot", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_ActorId_OccurredAt",
                schema: "displays",
                table: "audit_log",
                columns: new[] { "ActorId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_OccurredAt",
                schema: "displays",
                table: "audit_log",
                column: "OccurredAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_TargetType_TargetId_OccurredAt",
                schema: "displays",
                table: "audit_log",
                columns: new[] { "TargetType", "TargetId", "OccurredAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_TraceId",
                schema: "displays",
                table: "audit_log",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_display_asset_UploadedAt",
                schema: "displays",
                table: "display_asset",
                column: "UploadedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_display_configuration_EventId_Status",
                schema: "displays",
                table: "display_configuration",
                columns: new[] { "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_display_device_EventId_Status",
                schema: "displays",
                table: "display_device",
                columns: new[] { "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "ux_display_device_token_hash",
                schema: "displays",
                table: "display_device",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_display_pairing_Code_Status",
                schema: "displays",
                table: "display_pairing",
                columns: new[] { "Code", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_display_pairing_ExpiresAt",
                schema: "displays",
                table: "display_pairing",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_programme_slot_EventId_StartsAt",
                schema: "displays",
                table: "programme_slot",
                columns: new[] { "EventId", "StartsAt" });

            // Append-only at the database level (Spec 08 §13), same rule as events.audit_log: only a retention purge
            // that sets krafthaus.audit_purge = 'on' for its own transaction may delete rows.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION displays.audit_log_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND current_setting('krafthaus.audit_purge', true) = 'on' THEN
                        RETURN OLD;
                    END IF;

                    RAISE EXCEPTION 'displays.audit_log is append-only: % is not allowed', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_audit_log_append_only
                    BEFORE UPDATE OR DELETE ON displays.audit_log
                    FOR EACH ROW EXECUTE FUNCTION displays.audit_log_append_only();

                CREATE TRIGGER trg_audit_log_no_truncate
                    BEFORE TRUNCATE ON displays.audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION displays.audit_log_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_audit_log_no_truncate ON displays.audit_log;
                DROP TRIGGER IF EXISTS trg_audit_log_append_only ON displays.audit_log;
                DROP FUNCTION IF EXISTS displays.audit_log_append_only();
                """);

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "displays");

            migrationBuilder.DropTable(
                name: "display_asset",
                schema: "displays");

            migrationBuilder.DropTable(
                name: "display_configuration",
                schema: "displays");

            migrationBuilder.DropTable(
                name: "display_device",
                schema: "displays");

            migrationBuilder.DropTable(
                name: "display_pairing",
                schema: "displays");

            migrationBuilder.DropTable(
                name: "programme_slot",
                schema: "displays");
        }
    }
}
