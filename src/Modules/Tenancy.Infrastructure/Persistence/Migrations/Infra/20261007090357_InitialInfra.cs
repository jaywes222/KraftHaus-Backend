using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tenancy.Infrastructure.Persistence.Migrations.Infra;

/// <inheritdoc />
public partial class InitialInfra : Migration
{
    private static readonly string[] _kindTenantIdColumns = new[] { "Kind", "TenantId" };
    private static readonly string[] _tenantIdAttemptColumns = new[] { "TenantId", "Attempt" };

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "infra");

        migrationBuilder.CreateTable(
            name: "database_identity",
            schema: "infra",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                DatabaseName = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                CreatedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                LastModifiedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_database_identity", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "provisioning_attempt",
            schema: "infra",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                DatabaseName = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                Attempt = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                StartedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedOn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_provisioning_attempt", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_database_identity_Kind_TenantId",
            schema: "infra",
            table: "database_identity",
            columns: _kindTenantIdColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_provisioning_attempt_TenantId_Attempt",
            schema: "infra",
            table: "provisioning_attempt",
            columns: _tenantIdAttemptColumns,
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "database_identity",
            schema: "infra");

        migrationBuilder.DropTable(
            name: "provisioning_attempt",
            schema: "infra");
    }
}
