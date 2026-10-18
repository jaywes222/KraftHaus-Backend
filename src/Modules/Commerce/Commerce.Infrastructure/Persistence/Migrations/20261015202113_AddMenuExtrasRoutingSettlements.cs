using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuExtrasRoutingSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddOns",
                schema: "commerce",
                table: "menu_item",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                schema: "commerce",
                table: "menu_item",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                schema: "commerce",
                table: "menu_item",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImageVersion",
                schema: "commerce",
                table: "menu_item",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartsAt",
                schema: "commerce",
                table: "menu_item",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddOns",
                schema: "commerce",
                table: "kraft_order_line",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PaidIntoPlatformTill",
                schema: "commerce",
                table: "kraft_order",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PaymentRouting",
                schema: "commerce",
                table: "kraft_event",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "OwnTill");

            migrationBuilder.AddColumn<string>(
                name: "LogoPath",
                schema: "commerce",
                table: "kraft",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LogoVersion",
                schema: "commerce",
                table: "kraft",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "kraft_settlement",
                schema: "commerce",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kraft_settlement", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_kraft_settlement_EventId_KraftId",
                schema: "commerce",
                table: "kraft_settlement",
                columns: new[] { "EventId", "KraftId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kraft_settlement",
                schema: "commerce");

            migrationBuilder.DropColumn(
                name: "AddOns",
                schema: "commerce",
                table: "menu_item");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                schema: "commerce",
                table: "menu_item");

            migrationBuilder.DropColumn(
                name: "ImagePath",
                schema: "commerce",
                table: "menu_item");

            migrationBuilder.DropColumn(
                name: "ImageVersion",
                schema: "commerce",
                table: "menu_item");

            migrationBuilder.DropColumn(
                name: "StartsAt",
                schema: "commerce",
                table: "menu_item");

            migrationBuilder.DropColumn(
                name: "AddOns",
                schema: "commerce",
                table: "kraft_order_line");

            migrationBuilder.DropColumn(
                name: "PaidIntoPlatformTill",
                schema: "commerce",
                table: "kraft_order");

            migrationBuilder.DropColumn(
                name: "PaymentRouting",
                schema: "commerce",
                table: "kraft_event");

            migrationBuilder.DropColumn(
                name: "LogoPath",
                schema: "commerce",
                table: "kraft");

            migrationBuilder.DropColumn(
                name: "LogoVersion",
                schema: "commerce",
                table: "kraft");
        }
    }
}
