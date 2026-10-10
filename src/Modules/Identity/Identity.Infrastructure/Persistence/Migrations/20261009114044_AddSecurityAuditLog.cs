using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "security_audit_log",
                schema: "identity",
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
                    SubjectUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TargetId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_audit_log", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_security_audit_log_Action_OccurredAt",
                schema: "identity",
                table: "security_audit_log",
                columns: new[] { "Action", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_security_audit_log_ActorId_OccurredAt",
                schema: "identity",
                table: "security_audit_log",
                columns: new[] { "ActorId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_security_audit_log_OccurredAt",
                schema: "identity",
                table: "security_audit_log",
                column: "OccurredAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_security_audit_log_SubjectUserId_OccurredAt",
                schema: "identity",
                table: "security_audit_log",
                columns: new[] { "SubjectUserId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_security_audit_log_Target",
                schema: "identity",
                table: "security_audit_log",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_security_audit_log_TenantId_OccurredAt",
                schema: "identity",
                table: "security_audit_log",
                columns: new[] { "TenantId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_security_audit_log_TraceId",
                schema: "identity",
                table: "security_audit_log",
                column: "TraceId");

            // Append-only at the database level (Spec 08 §13). The application never updates or deletes audit
            // rows; this trigger also stops anyone else (including a superuser connection) from doing so.
            // The only escape hatch is a retention purge that opts in for its own transaction by setting the
            // krafthaus.audit_purge setting to 'on' (SET LOCAL) before deleting expired rows.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION identity.security_audit_log_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND current_setting('krafthaus.audit_purge', true) = 'on' THEN
                        RETURN OLD;
                    END IF;

                    RAISE EXCEPTION 'identity.security_audit_log is append-only: % is not allowed', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_security_audit_log_append_only
                    BEFORE UPDATE OR DELETE ON identity.security_audit_log
                    FOR EACH ROW EXECUTE FUNCTION identity.security_audit_log_append_only();

                CREATE TRIGGER trg_security_audit_log_no_truncate
                    BEFORE TRUNCATE ON identity.security_audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION identity.security_audit_log_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_security_audit_log_no_truncate ON identity.security_audit_log;
                DROP TRIGGER IF EXISTS trg_security_audit_log_append_only ON identity.security_audit_log;
                DROP FUNCTION IF EXISTS identity.security_audit_log_append_only();
                """);

            migrationBuilder.DropTable(
                name: "security_audit_log",
                schema: "identity");
        }
    }
}
