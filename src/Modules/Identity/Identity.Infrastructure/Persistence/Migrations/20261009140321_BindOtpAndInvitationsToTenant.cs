using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BindOtpAndInvitationsToTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_otp_challenge_Channel_Destination_Purpose_IssuedAt",
                schema: "identity",
                table: "otp_challenge");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "identity",
                table: "staff_invitation",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "identity",
                table: "otp_challenge",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_invitation_TenantId_Status",
                schema: "identity",
                table: "staff_invitation",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_otp_challenge_TenantId_Channel_Destination_Purpose_IssuedAt",
                schema: "identity",
                table: "otp_challenge",
                columns: new[] { "TenantId", "Channel", "Destination", "Purpose", "IssuedAt" },
                descending: new[] { false, false, false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_staff_invitation_TenantId_Status",
                schema: "identity",
                table: "staff_invitation");

            migrationBuilder.DropIndex(
                name: "IX_otp_challenge_TenantId_Channel_Destination_Purpose_IssuedAt",
                schema: "identity",
                table: "otp_challenge");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "identity",
                table: "staff_invitation");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "identity",
                table: "otp_challenge");

            migrationBuilder.CreateIndex(
                name: "IX_otp_challenge_Channel_Destination_Purpose_IssuedAt",
                schema: "identity",
                table: "otp_challenge",
                columns: new[] { "Channel", "Destination", "Purpose", "IssuedAt" },
                descending: new[] { false, false, false, true });
        }
    }
}
