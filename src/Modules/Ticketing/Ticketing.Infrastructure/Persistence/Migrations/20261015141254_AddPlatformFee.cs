using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ticketing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FeeWaived",
                schema: "ticketing",
                table: "order",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformFee",
                schema: "ticketing",
                table: "order",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Subtotal",
                schema: "ticketing",
                table: "order",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            // Orders placed before the fee: everything paid was the subtotal.
            migrationBuilder.Sql("""UPDATE ticketing."order" SET "Subtotal" = "Total" WHERE "Subtotal" = 0;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeeWaived",
                schema: "ticketing",
                table: "order");

            migrationBuilder.DropColumn(
                name: "PlatformFee",
                schema: "ticketing",
                table: "order");

            migrationBuilder.DropColumn(
                name: "Subtotal",
                schema: "ticketing",
                table: "order");
        }
    }
}
