using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ServiceFee",
                schema: "commerce",
                table: "kraft_order",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ServiceFeeWaived",
                schema: "commerce",
                table: "kraft_order",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Subtotal",
                schema: "commerce",
                table: "kraft_order",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            // Orders placed before the fee: everything paid was the subtotal.
            migrationBuilder.Sql("""UPDATE commerce.kraft_order SET "Subtotal" = "Total" WHERE "Subtotal" = 0;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServiceFee",
                schema: "commerce",
                table: "kraft_order");

            migrationBuilder.DropColumn(
                name: "ServiceFeeWaived",
                schema: "commerce",
                table: "kraft_order");

            migrationBuilder.DropColumn(
                name: "Subtotal",
                schema: "commerce",
                table: "kraft_order");
        }
    }
}
