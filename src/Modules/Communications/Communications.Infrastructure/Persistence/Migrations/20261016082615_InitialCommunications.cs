using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Communications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCommunications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "communications");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "communications",
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
                name: "broadcast",
                schema: "communications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Audience = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Body = table.Column<string>(type: "character varying(459)", maxLength: 459, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Recipients = table.Column<int>(type: "integer", nullable: false),
                    Skipped = table.Column<int>(type: "integer", nullable: false),
                    Sent = table.Column<int>(type: "integer", nullable: false),
                    Failed = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broadcast", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "message",
                schema: "communications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BroadcastId = table.Column<Guid>(type: "uuid", nullable: true),
                    Recipient = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProviderStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "message_template",
                schema: "communications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Body = table.Column<string>(type: "character varying(459)", maxLength: 459, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_template", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "opt_out",
                schema: "communications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OptedOutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OptedInAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opt_out", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_ActorId_OccurredAt",
                schema: "communications",
                table: "audit_log",
                columns: new[] { "ActorId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_OccurredAt",
                schema: "communications",
                table: "audit_log",
                column: "OccurredAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_TargetType_TargetId_OccurredAt",
                schema: "communications",
                table: "audit_log",
                columns: new[] { "TargetType", "TargetId", "OccurredAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_TraceId",
                schema: "communications",
                table: "audit_log",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_broadcast_EventId_CreatedOn",
                schema: "communications",
                table: "broadcast",
                columns: new[] { "EventId", "CreatedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_broadcast_Status_ScheduledFor",
                schema: "communications",
                table: "broadcast",
                columns: new[] { "Status", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_message_BroadcastId_Status_QueuedAt",
                schema: "communications",
                table: "message",
                columns: new[] { "BroadcastId", "Status", "QueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_message_ProviderMessageId",
                schema: "communications",
                table: "message",
                column: "ProviderMessageId",
                filter: "\"ProviderMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_message_QueuedAt",
                schema: "communications",
                table: "message",
                column: "QueuedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_message_Recipient_QueuedAt",
                schema: "communications",
                table: "message",
                columns: new[] { "Recipient", "QueuedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_opt_out_IsActive_OptedOutAt",
                schema: "communications",
                table: "opt_out",
                columns: new[] { "IsActive", "OptedOutAt" });

            migrationBuilder.CreateIndex(
                name: "IX_opt_out_Phone",
                schema: "communications",
                table: "opt_out",
                column: "Phone",
                unique: true);

            // Append-only at the database level (Spec 08 §13), same rule as the other modules' audit logs: only a retention
            // purge that sets krafthaus.audit_purge = 'on' for its own transaction may delete rows.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION communications.audit_log_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND current_setting('krafthaus.audit_purge', true) = 'on' THEN
                        RETURN OLD;
                    END IF;

                    RAISE EXCEPTION 'communications.audit_log is append-only: % is not allowed', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_audit_log_append_only
                    BEFORE UPDATE OR DELETE ON communications.audit_log
                    FOR EACH ROW EXECUTE FUNCTION communications.audit_log_append_only();

                CREATE TRIGGER trg_audit_log_no_truncate
                    BEFORE TRUNCATE ON communications.audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION communications.audit_log_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_audit_log_no_truncate ON communications.audit_log;
                DROP TRIGGER IF EXISTS trg_audit_log_append_only ON communications.audit_log;
                DROP FUNCTION IF EXISTS communications.audit_log_append_only();
                """);

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "broadcast",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "message",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "message_template",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "opt_out",
                schema: "communications");
        }
    }
}
