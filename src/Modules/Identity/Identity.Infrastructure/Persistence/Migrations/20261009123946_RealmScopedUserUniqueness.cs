using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RealmScopedUserUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_application_user_Scope_Email",
                schema: "identity",
                table: "application_user");

            migrationBuilder.DropIndex(
                name: "IX_application_user_Scope_Phone",
                schema: "identity",
                table: "application_user");

            migrationBuilder.CreateIndex(
                name: "IX_application_user_Scope_TenantId_Email",
                schema: "identity",
                table: "application_user",
                columns: new[] { "Scope", "TenantId", "Email" },
                unique: true,
                filter: "\"Email\" IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_application_user_Scope_TenantId_Phone",
                schema: "identity",
                table: "application_user",
                columns: new[] { "Scope", "TenantId", "Phone" },
                unique: true,
                filter: "\"Phone\" IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_application_user_Scope_TenantId_Email",
                schema: "identity",
                table: "application_user");

            migrationBuilder.DropIndex(
                name: "IX_application_user_Scope_TenantId_Phone",
                schema: "identity",
                table: "application_user");

            migrationBuilder.CreateIndex(
                name: "IX_application_user_Scope_Email",
                schema: "identity",
                table: "application_user",
                columns: new[] { "Scope", "Email" },
                unique: true,
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_application_user_Scope_Phone",
                schema: "identity",
                table: "application_user",
                columns: new[] { "Scope", "Phone" },
                unique: true,
                filter: "\"Phone\" IS NOT NULL");
        }
    }
}
