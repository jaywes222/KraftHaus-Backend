using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ticketing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdersTicketsAndCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "capacity_bucket",
                schema: "ticketing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RefId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Held = table.Column<int>(type: "integer", nullable: false),
                    Sold = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capacity_bucket", x => x.Id);
                    table.CheckConstraint("ck_capacity_bucket_counts", "\"Held\" >= 0 AND \"Sold\" >= 0 AND \"Total\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "capacity_hold",
                schema: "ticketing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RefId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capacity_hold", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "order",
                schema: "ticketing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerPhone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PayerPhone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Admissions = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PlacedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HoldExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CurrentPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastPaymentFailure = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PaidByPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttentionReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "ticketing",
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
                schema: "ticketing",
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
                name: "ticket",
                schema: "ticketing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventDateId = table.Column<Guid>(type: "uuid", nullable: false),
                    TierId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    HolderPhone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    HolderName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EventTitle = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    TierName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DateLabel = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    DateStartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DoorsOpenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VenueName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TransferCount = table.Column<int>(type: "integer", nullable: false),
                    CheckedInAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CheckedInBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckInMethod = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "order_line",
                schema: "ticketing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EventDateId = table.Column<Guid>(type: "uuid", nullable: false),
                    TierId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Units = table.Column<int>(type: "integer", nullable: false),
                    AdmissionsPerUnit = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_line", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_line_order_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ticketing",
                        principalTable: "order",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_capacity_bucket_EventId",
                schema: "ticketing",
                table: "capacity_bucket",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_capacity_bucket_Kind_RefId",
                schema: "ticketing",
                table: "capacity_bucket",
                columns: new[] { "Kind", "RefId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_capacity_hold_OrderId_Status",
                schema: "ticketing",
                table: "capacity_hold",
                columns: new[] { "OrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_order_CustomerUserId_EventId_Status",
                schema: "ticketing",
                table: "order",
                columns: new[] { "CustomerUserId", "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_order_CustomerUserId_IdempotencyKey",
                schema: "ticketing",
                table: "order",
                columns: new[] { "CustomerUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_EventId_Status_PlacedAt",
                schema: "ticketing",
                table: "order",
                columns: new[] { "EventId", "Status", "PlacedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_order_Reference",
                schema: "ticketing",
                table: "order",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_Status_HoldExpiresAt",
                schema: "ticketing",
                table: "order",
                columns: new[] { "Status", "HoldExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_order_line_OrderId",
                schema: "ticketing",
                table: "order_line",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "ticketing",
                table: "outbox_message",
                column: "AvailableAt",
                filter: "\"ProcessedAt\" IS NULL AND \"DeadLetteredAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_Code",
                schema: "ticketing",
                table: "ticket",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ticket_EventDateId_Status",
                schema: "ticketing",
                table: "ticket",
                columns: new[] { "EventDateId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_HolderPhone",
                schema: "ticketing",
                table: "ticket",
                column: "HolderPhone");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_OrderId",
                schema: "ticketing",
                table: "ticket",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capacity_bucket",
                schema: "ticketing");

            migrationBuilder.DropTable(
                name: "capacity_hold",
                schema: "ticketing");

            migrationBuilder.DropTable(
                name: "order_line",
                schema: "ticketing");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "ticketing");

            migrationBuilder.DropTable(
                name: "processed_message",
                schema: "ticketing");

            migrationBuilder.DropTable(
                name: "ticket",
                schema: "ticketing");

            migrationBuilder.DropTable(
                name: "order",
                schema: "ticketing");
        }
    }
}
