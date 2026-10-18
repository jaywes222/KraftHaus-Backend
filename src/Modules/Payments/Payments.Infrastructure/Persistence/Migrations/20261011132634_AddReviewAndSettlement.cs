using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewAndSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewDecision",
                schema: "payments",
                table: "payment",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                schema: "payments",
                table: "payment",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                schema: "payments",
                table: "payment",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedBy",
                schema: "payments",
                table: "payment",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "settlement_record",
                schema: "payments",
                columns: table => new
                {
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settlement_record", x => x.PaymentId);
                    table.ForeignKey(
                        name: "FK_settlement_record_payment_PaymentId",
                        column: x => x.PaymentId,
                        principalSchema: "payments",
                        principalTable: "payment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_settlement_record_Status_SettledAt",
                schema: "payments",
                table: "settlement_record",
                columns: new[] { "Status", "SettledAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settlement_record",
                schema: "payments");

            migrationBuilder.DropColumn(
                name: "ReviewDecision",
                schema: "payments",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                schema: "payments",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                schema: "payments",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                schema: "payments",
                table: "payment");
        }
    }
}
