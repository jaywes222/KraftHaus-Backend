using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenancy");

            migrationBuilder.CreateTable(
                name: "tenant",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IdentityScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DatabaseName = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    aggregate_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "policy_version",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    PolicyHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_version", x => x.Id);
                    table.ForeignKey(
                        name: "FK_policy_version_tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_host",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostName = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Surface = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_host", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_host_tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_policy_version_TenantId_PolicyKey_VersionNumber",
                schema: "tenancy",
                table: "policy_version",
                columns: new[] { "TenantId", "PolicyKey", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_Slug",
                schema: "tenancy",
                table: "tenant",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_host_HostName",
                schema: "tenancy",
                table: "tenant_host",
                column: "HostName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_host_TenantId",
                schema: "tenancy",
                table: "tenant_host",
                column: "TenantId",
                unique: true,
                filter: "\"IsPrimary\" = TRUE AND \"Enabled\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "policy_version",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "tenant_host",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "tenant",
                schema: "tenancy");
        }
    }
}
