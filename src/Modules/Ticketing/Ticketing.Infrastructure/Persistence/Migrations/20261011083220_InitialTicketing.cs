using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ticketing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTicketing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ticketing");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "ticketing",
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
                name: "waitlist_entry",
                schema: "ticketing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Age = table.Column<int>(type: "integer", nullable: false),
                    Gender = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Location = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    PrivacyPolicyVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PrivacyAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MarketingConsent = table.Column<bool>(type: "boolean", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmationSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waitlist_entry", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_ActorId_OccurredAt",
                schema: "ticketing",
                table: "audit_log",
                columns: new[] { "ActorId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_OccurredAt",
                schema: "ticketing",
                table: "audit_log",
                column: "OccurredAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_TargetType_TargetId_OccurredAt",
                schema: "ticketing",
                table: "audit_log",
                columns: new[] { "TargetType", "TargetId", "OccurredAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_TraceId",
                schema: "ticketing",
                table: "audit_log",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entry_EventId_JoinedAt",
                schema: "ticketing",
                table: "waitlist_entry",
                columns: new[] { "EventId", "JoinedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entry_Phone",
                schema: "ticketing",
                table: "waitlist_entry",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "ux_waitlist_entry_event_phone",
                schema: "ticketing",
                table: "waitlist_entry",
                columns: new[] { "EventId", "Phone" },
                unique: true);

            // Append-only at the database level (Spec 08 §13), same rule as events.audit_log: only a retention purge
            // that sets krafthaus.audit_purge = 'on' for its own transaction may delete rows.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION ticketing.audit_log_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND current_setting('krafthaus.audit_purge', true) = 'on' THEN
                        RETURN OLD;
                    END IF;

                    RAISE EXCEPTION 'ticketing.audit_log is append-only: % is not allowed', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_audit_log_append_only
                    BEFORE UPDATE OR DELETE ON ticketing.audit_log
                    FOR EACH ROW EXECUTE FUNCTION ticketing.audit_log_append_only();

                CREATE TRIGGER trg_audit_log_no_truncate
                    BEFORE TRUNCATE ON ticketing.audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION ticketing.audit_log_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_audit_log_no_truncate ON ticketing.audit_log;
                DROP TRIGGER IF EXISTS trg_audit_log_append_only ON ticketing.audit_log;
                DROP FUNCTION IF EXISTS ticketing.audit_log_append_only();
                """);

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "ticketing");

            migrationBuilder.DropTable(
                name: "waitlist_entry",
                schema: "ticketing");
        }
    }
}
