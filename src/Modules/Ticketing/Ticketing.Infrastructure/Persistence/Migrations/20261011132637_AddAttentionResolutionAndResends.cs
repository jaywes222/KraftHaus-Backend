using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ticketing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttentionResolutionAndResends : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConfirmationAttempts",
                schema: "ticketing",
                table: "waitlist_entry",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AttentionResolution",
                schema: "ticketing",
                table: "order",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AttentionResolvedAt",
                schema: "ticketing",
                table: "order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AttentionResolvedBy",
                schema: "ticketing",
                table: "order",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmationAttempts",
                schema: "ticketing",
                table: "waitlist_entry");

            migrationBuilder.DropColumn(
                name: "AttentionResolution",
                schema: "ticketing",
                table: "order");

            migrationBuilder.DropColumn(
                name: "AttentionResolvedAt",
                schema: "ticketing",
                table: "order");

            migrationBuilder.DropColumn(
                name: "AttentionResolvedBy",
                schema: "ticketing",
                table: "order");
        }
    }
}
