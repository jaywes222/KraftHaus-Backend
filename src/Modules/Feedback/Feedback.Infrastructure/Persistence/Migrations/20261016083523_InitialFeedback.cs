using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Feedback.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "feedback");

            migrationBuilder.CreateTable(
                name: "rating",
                schema: "feedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventDateId = table.Column<Guid>(type: "uuid", nullable: false),
                    DedupeHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Overall = table.Column<int>(type: "integer", nullable: false),
                    Music = table.Column<int>(type: "integer", nullable: true),
                    Food = table.Column<int>(type: "integer", nullable: true),
                    Venue = table.Column<int>(type: "integer", nullable: true),
                    Organisation = table.Column<int>(type: "integer", nullable: true),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SubmittedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CommentHidden = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rating", x => x.Id);
                    table.CheckConstraint("ck_rating_aspects", "(\"Music\" IS NULL OR \"Music\" BETWEEN 1 AND 5) AND (\"Food\" IS NULL OR \"Food\" BETWEEN 1 AND 5) AND (\"Venue\" IS NULL OR \"Venue\" BETWEEN 1 AND 5) AND (\"Organisation\" IS NULL OR \"Organisation\" BETWEEN 1 AND 5)");
                    table.CheckConstraint("ck_rating_overall", "\"Overall\" BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateIndex(
                name: "IX_rating_EventId_EventDateId",
                schema: "feedback",
                table: "rating",
                columns: new[] { "EventId", "EventDateId" });

            migrationBuilder.CreateIndex(
                name: "ux_rating_dedupe_hash",
                schema: "feedback",
                table: "rating",
                column: "DedupeHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rating",
                schema: "feedback");
        }
    }
}
