using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Displays.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGallery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gallery_photo",
                schema: "displays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Caption = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gallery_photo", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gallery_photo_AssetId",
                schema: "displays",
                table: "gallery_photo",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_gallery_photo_EventId_SortOrder",
                schema: "displays",
                table: "gallery_photo",
                columns: new[] { "EventId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gallery_photo",
                schema: "displays");
        }
    }
}
