using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "payments",
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
                name: "outbox_message",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    TraceParent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_message", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payment",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AccountReference = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Description = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CallbackTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProviderCheckoutId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MpesaReceipt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResultCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResultDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProviderErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReportedAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PromptSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CheckCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_ActorId_OccurredAt",
                schema: "payments",
                table: "audit_log",
                columns: new[] { "ActorId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_OccurredAt",
                schema: "payments",
                table: "audit_log",
                column: "OccurredAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_TargetType_TargetId_OccurredAt",
                schema: "payments",
                table: "audit_log",
                columns: new[] { "TargetType", "TargetId", "OccurredAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_TraceId",
                schema: "payments",
                table: "audit_log",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "payments",
                table: "outbox_message",
                column: "AvailableAt",
                filter: "\"ProcessedAt\" IS NULL AND \"DeadLetteredAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_CallbackTokenHash",
                schema: "payments",
                table: "payment",
                column: "CallbackTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_ProviderTransactionId",
                schema: "payments",
                table: "payment",
                column: "ProviderTransactionId",
                unique: true,
                filter: "\"ProviderTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_Purpose_ReferenceId",
                schema: "payments",
                table: "payment",
                columns: new[] { "Purpose", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_Status_CreatedAt",
                schema: "payments",
                table: "payment",
                columns: new[] { "Status", "CreatedAt" });

            // Append-only at the database level (Spec 08 §13), same rule as the other audit tables.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION payments.audit_log_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND current_setting('krafthaus.audit_purge', true) = 'on' THEN
                        RETURN OLD;
                    END IF;

                    RAISE EXCEPTION 'payments.audit_log is append-only: % is not allowed', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_audit_log_append_only
                    BEFORE UPDATE OR DELETE ON payments.audit_log
                    FOR EACH ROW EXECUTE FUNCTION payments.audit_log_append_only();

                CREATE TRIGGER trg_audit_log_no_truncate
                    BEFORE TRUNCATE ON payments.audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION payments.audit_log_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_audit_log_no_truncate ON payments.audit_log;
                DROP TRIGGER IF EXISTS trg_audit_log_append_only ON payments.audit_log;
                DROP FUNCTION IF EXISTS payments.audit_log_append_only();
                """);

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment",
                schema: "payments");
        }
    }
}
