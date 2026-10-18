using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCommerce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "commerce");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "commerce",
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
                name: "kraft",
                schema: "commerce",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Slug = table.Column<string>(type: "character varying(68)", maxLength: 68, nullable: false),
                    Discipline = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kraft", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "kraft_order",
                schema: "commerce",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    KraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    KraftName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reference = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    PickupCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerPhone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PayerPhone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    PlatformFee = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    KraftRevenue = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PlacedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HoldExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CurrentPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastPaymentFailure = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MpesaReceipt = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PreparingAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReadyAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CollectedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UncollectedFlaggedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttentionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AmountToRefund = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AttentionResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttentionResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AttentionResolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kraft_order", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "menu_item",
                schema: "commerce",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    LiveStock = table.Column<int>(type: "integer", nullable: true),
                    ReservationAllocation = table.Column<int>(type: "integer", nullable: true),
                    AgeRestricted = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Available = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_item", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "commerce",
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
                name: "processed_message",
                schema: "commerce",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Handler = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_message", x => new { x.MessageId, x.Handler });
                });

            migrationBuilder.CreateTable(
                name: "stock_bucket",
                schema: "commerce",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    KraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: true),
                    Held = table.Column<int>(type: "integer", nullable: false),
                    Sold = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_bucket", x => x.Id);
                    table.CheckConstraint("ck_stock_bucket_counts", "\"Held\" >= 0 AND \"Sold\" >= 0 AND (\"Total\" IS NULL OR \"Total\" >= 0)");
                });

            migrationBuilder.CreateTable(
                name: "stock_hold",
                schema: "commerce",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_hold", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "kraft_brand",
                schema: "commerce",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    KraftId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kraft_brand", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kraft_brand_kraft_KraftId",
                        column: x => x.KraftId,
                        principalSchema: "commerce",
                        principalTable: "kraft",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "kraft_event",
                schema: "commerce",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    Stall = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    KraftId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kraft_event", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kraft_event_kraft_KraftId",
                        column: x => x.KraftId,
                        principalSchema: "commerce",
                        principalTable: "kraft",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "krafter",
                schema: "commerce",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Role = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Bio = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Instagram = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    KraftId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_krafter", x => x.Id);
                    table.ForeignKey(
                        name: "FK_krafter_kraft_KraftId",
                        column: x => x.KraftId,
                        principalSchema: "commerce",
                        principalTable: "kraft",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "kraft_order_line",
                schema: "commerce",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AgeRestricted = table.Column<bool>(type: "boolean", nullable: false),
                    Unavailable = table.Column<bool>(type: "boolean", nullable: false),
                    UnavailableNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UnavailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kraft_order_line", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kraft_order_line_kraft_order_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "commerce",
                        principalTable: "kraft_order",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_ActorId_OccurredAt",
                schema: "commerce",
                table: "audit_log",
                columns: new[] { "ActorId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_OccurredAt",
                schema: "commerce",
                table: "audit_log",
                column: "OccurredAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_TargetType_TargetId_OccurredAt",
                schema: "commerce",
                table: "audit_log",
                columns: new[] { "TargetType", "TargetId", "OccurredAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_TraceId",
                schema: "commerce",
                table: "audit_log",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_kraft_Status_Name",
                schema: "commerce",
                table: "kraft",
                columns: new[] { "Status", "Name" });

            migrationBuilder.CreateIndex(
                name: "ux_kraft_slug",
                schema: "commerce",
                table: "kraft",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kraft_brand_KraftId",
                schema: "commerce",
                table: "kraft_brand",
                column: "KraftId");

            migrationBuilder.CreateIndex(
                name: "IX_kraft_event_EventId",
                schema: "commerce",
                table: "kraft_event",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_kraft_event_KraftId_EventId",
                schema: "commerce",
                table: "kraft_event",
                columns: new[] { "KraftId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kraft_order_CheckoutGroupId",
                schema: "commerce",
                table: "kraft_order",
                column: "CheckoutGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_kraft_order_CustomerUserId_IdempotencyKey_KraftId",
                schema: "commerce",
                table: "kraft_order",
                columns: new[] { "CustomerUserId", "IdempotencyKey", "KraftId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kraft_order_CustomerUserId_PlacedAt",
                schema: "commerce",
                table: "kraft_order",
                columns: new[] { "CustomerUserId", "PlacedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_kraft_order_EventId_PlacedAt",
                schema: "commerce",
                table: "kraft_order",
                columns: new[] { "EventId", "PlacedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_kraft_order_KraftId_EventId_Status",
                schema: "commerce",
                table: "kraft_order",
                columns: new[] { "KraftId", "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_kraft_order_KraftId_PickupCode",
                schema: "commerce",
                table: "kraft_order",
                columns: new[] { "KraftId", "PickupCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kraft_order_Reference",
                schema: "commerce",
                table: "kraft_order",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kraft_order_Status_HoldExpiresAt",
                schema: "commerce",
                table: "kraft_order",
                columns: new[] { "Status", "HoldExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_kraft_order_line_MenuItemId",
                schema: "commerce",
                table: "kraft_order_line",
                column: "MenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_kraft_order_line_OrderId",
                schema: "commerce",
                table: "kraft_order_line",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_krafter_KraftId",
                schema: "commerce",
                table: "krafter",
                column: "KraftId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_item_EventId_Status",
                schema: "commerce",
                table: "menu_item",
                columns: new[] { "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_item_KraftId_EventId",
                schema: "commerce",
                table: "menu_item",
                columns: new[] { "KraftId", "EventId" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "commerce",
                table: "outbox_message",
                column: "AvailableAt",
                filter: "\"ProcessedAt\" IS NULL AND \"DeadLetteredAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_stock_bucket_EventId_KraftId",
                schema: "commerce",
                table: "stock_bucket",
                columns: new[] { "EventId", "KraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_bucket_Kind_MenuItemId",
                schema: "commerce",
                table: "stock_bucket",
                columns: new[] { "Kind", "MenuItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_hold_OrderId_Status",
                schema: "commerce",
                table: "stock_hold",
                columns: new[] { "OrderId", "Status" });

            // Append-only at the database level (Spec 08 §13), same rule as events.audit_log: only a retention purge
            // that sets krafthaus.audit_purge = 'on' for its own transaction may delete rows.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION commerce.audit_log_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND current_setting('krafthaus.audit_purge', true) = 'on' THEN
                        RETURN OLD;
                    END IF;

                    RAISE EXCEPTION 'commerce.audit_log is append-only: % is not allowed', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_audit_log_append_only
                    BEFORE UPDATE OR DELETE ON commerce.audit_log
                    FOR EACH ROW EXECUTE FUNCTION commerce.audit_log_append_only();

                CREATE TRIGGER trg_audit_log_no_truncate
                    BEFORE TRUNCATE ON commerce.audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION commerce.audit_log_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_audit_log_no_truncate ON commerce.audit_log;
                DROP TRIGGER IF EXISTS trg_audit_log_append_only ON commerce.audit_log;
                DROP FUNCTION IF EXISTS commerce.audit_log_append_only();
                """);

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "commerce");

            migrationBuilder.DropTable(
                name: "kraft_brand",
                schema: "commerce");

            migrationBuilder.DropTable(
                name: "kraft_event",
                schema: "commerce");

            migrationBuilder.DropTable(
                name: "kraft_order_line",
                schema: "commerce");

            migrationBuilder.DropTable(
                name: "krafter",
                schema: "commerce");

            migrationBuilder.DropTable(
                name: "menu_item",
                schema: "commerce");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "commerce");

            migrationBuilder.DropTable(
                name: "processed_message",
                schema: "commerce");

            migrationBuilder.DropTable(
                name: "stock_bucket",
                schema: "commerce");

            migrationBuilder.DropTable(
                name: "stock_hold",
                schema: "commerce");

            migrationBuilder.DropTable(
                name: "kraft_order",
                schema: "commerce");

            migrationBuilder.DropTable(
                name: "kraft",
                schema: "commerce");
        }
    }
}
