using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentDestinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DestinationId",
                schema: "payments",
                table: "payment",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderChannelId",
                schema: "payments",
                table: "payment",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payment_destination",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    KraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ShortcodeType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Shortcode = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProviderChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    TestStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TestPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_destination", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "processed_message",
                schema: "payments",
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

            migrationBuilder.CreateIndex(
                name: "IX_payment_DestinationId",
                schema: "payments",
                table: "payment",
                column: "DestinationId",
                filter: "\"DestinationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_destination_EventId_Status",
                schema: "payments",
                table: "payment_destination",
                columns: new[] { "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_destination_TestPaymentId",
                schema: "payments",
                table: "payment_destination",
                column: "TestPaymentId",
                filter: "\"TestPaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payment_destination_active_till",
                schema: "payments",
                table: "payment_destination",
                columns: new[] { "KraftId", "EventId" },
                unique: true,
                filter: "\"Status\" IN ('Submitted', 'Approved')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_destination",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "processed_message",
                schema: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payment_DestinationId",
                schema: "payments",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "DestinationId",
                schema: "payments",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "ProviderChannelId",
                schema: "payments",
                table: "payment");
        }
    }
}
