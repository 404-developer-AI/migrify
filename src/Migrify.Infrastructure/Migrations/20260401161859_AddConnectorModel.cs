using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Migrify.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add SourceConnectorType to Projects (default ManualImap)
            migrationBuilder.AddColumn<string>(
                name: "SourceConnectorType",
                table: "Projects",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "ManualImap");

            // 2. Add HasImapOverride to MigrationJobs
            migrationBuilder.AddColumn<bool>(
                name: "HasImapOverride",
                table: "MigrationJobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // 3. Set HasImapOverride = true for existing jobs that have ImapSettings
            migrationBuilder.Sql("""
                UPDATE "MigrationJobs" SET "HasImapOverride" = true
                WHERE "Id" IN (SELECT "MigrationJobId" FROM "ImapSettings")
                """);

            // 4. Add ProjectId column to M365Settings (nullable first for data migration)
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "M365Settings",
                type: "uuid",
                nullable: true);

            // 5. Populate ProjectId from the MigrationJob's ProjectId (take one per project)
            migrationBuilder.Sql("""
                UPDATE "M365Settings" ms
                SET "ProjectId" = j."ProjectId"
                FROM "MigrationJobs" j
                WHERE j."Id" = ms."MigrationJobId"
                """);

            // 6. Delete duplicate M365Settings per project (keep the most recent one per project)
            migrationBuilder.Sql("""
                DELETE FROM "M365Settings"
                WHERE "Id" NOT IN (
                    SELECT DISTINCT ON ("ProjectId") "Id"
                    FROM "M365Settings"
                    WHERE "ProjectId" IS NOT NULL
                    ORDER BY "ProjectId", "Id"
                )
                """);

            // 7. Drop old FK and MigrationJobId column
            migrationBuilder.DropForeignKey(
                name: "FK_M365Settings_MigrationJobs_MigrationJobId",
                table: "M365Settings");

            migrationBuilder.DropIndex(
                name: "IX_M365Settings_MigrationJobId",
                table: "M365Settings");

            migrationBuilder.DropColumn(
                name: "MigrationJobId",
                table: "M365Settings");

            // 8. Make ProjectId non-nullable and add unique index + FK
            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                table: "M365Settings",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_M365Settings_ProjectId",
                table: "M365Settings",
                column: "ProjectId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_M365Settings_Projects_ProjectId",
                table: "M365Settings",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // 9. Create GoogleWorkspaceSettings table
            migrationBuilder.CreateTable(
                name: "GoogleWorkspaceSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceAccountEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EncryptedPrivateKey = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    TokenUri = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ImpersonationEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoogleWorkspaceSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoogleWorkspaceSettings_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoogleWorkspaceSettings_ProjectId",
                table: "GoogleWorkspaceSettings",
                column: "ProjectId",
                unique: true);

            // 10. Create DiscoveredMailboxes table
            migrationBuilder.CreateTable(
                name: "DiscoveredMailboxes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Side = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DiscoveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveredMailboxes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveredMailboxes_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredMailboxes_ProjectId_Side",
                table: "DiscoveredMailboxes",
                columns: new[] { "ProjectId", "Side" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_M365Settings_Projects_ProjectId",
                table: "M365Settings");

            migrationBuilder.DropTable(
                name: "DiscoveredMailboxes");

            migrationBuilder.DropTable(
                name: "GoogleWorkspaceSettings");

            migrationBuilder.DropColumn(
                name: "SourceConnectorType",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "HasImapOverride",
                table: "MigrationJobs");

            migrationBuilder.DropIndex(
                name: "IX_M365Settings_ProjectId",
                table: "M365Settings");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "M365Settings");

            migrationBuilder.AddColumn<Guid>(
                name: "MigrationJobId",
                table: "M365Settings",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.CreateIndex(
                name: "IX_M365Settings_MigrationJobId",
                table: "M365Settings",
                column: "MigrationJobId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_M365Settings_MigrationJobs_MigrationJobId",
                table: "M365Settings",
                column: "MigrationJobId",
                principalTable: "MigrationJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
