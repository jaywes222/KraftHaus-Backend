using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ticketing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallmentsAndAttendees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttendeeName",
                schema: "ticketing",
                table: "ticket",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountPaid",
                schema: "ticketing",
                table: "order",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BalanceDueAt",
                schema: "ticketing",
                table: "order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BalanceRemindersSent",
                schema: "ticketing",
                table: "order",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                schema: "ticketing",
                table: "order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositAmount",
                schema: "ticketing",
                table: "order",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DepositPaidAt",
                schema: "ticketing",
                table: "order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentPlan",
                schema: "ticketing",
                table: "order",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Full");

            migrationBuilder.CreateIndex(
                name: "IX_order_Status_BalanceDueAt",
                schema: "ticketing",
                table: "order",
                columns: new[] { "Status", "BalanceDueAt" });

            // Orders placed before installments existed were all paid in full.
            migrationBuilder.Sql(
                """
                UPDATE ticketing."order" SET "DepositAmount" = "Total";
                UPDATE ticketing."order" SET "AmountPaid" = "Total" WHERE "Status" = 'Paid';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_order_Status_BalanceDueAt",
                schema: "ticketing",
                table: "order");

            migrationBuilder.DropColumn(
                name: "AttendeeName",
                schema: "ticketing",
                table: "ticket");

            migrationBuilder.DropColumn(
                name: "AmountPaid",
                schema: "ticketing",
                table: "order");

            migrationBuilder.DropColumn(
                name: "BalanceDueAt",
                schema: "ticketing",
                table: "order");

            migrationBuilder.DropColumn(
                name: "BalanceRemindersSent",
                schema: "ticketing",
                table: "order");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                schema: "ticketing",
                table: "order");

            migrationBuilder.DropColumn(
                name: "DepositAmount",
                schema: "ticketing",
                table: "order");

            migrationBuilder.DropColumn(
                name: "DepositPaidAt",
                schema: "ticketing",
                table: "order");

            migrationBuilder.DropColumn(
                name: "PaymentPlan",
                schema: "ticketing",
                table: "order");
        }
    }
}
