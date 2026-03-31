using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Migrify.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveAuthToMigrationJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImapSettings_Projects_ProjectId",
                table: "ImapSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_M365Settings_Projects_ProjectId",
                table: "M365Settings");

            migrationBuilder.DropColumn(
                name: "EncryptedPassword",
                table: "MigrationJobs");

            migrationBuilder.DropColumn(
                name: "UseOwnCredentials",
                table: "MigrationJobs");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "MigrationJobs");

            // Data migration: update ImapSettings.ProjectId to MigrationJob.Id
            migrationBuilder.Sql(@"
                UPDATE ""ImapSettings"" i
                SET ""ProjectId"" = j.""Id""
                FROM ""MigrationJobs"" j
                WHERE i.""ProjectId"" = j.""ProjectId""
            ");

            // Data migration: update M365Settings.ProjectId to MigrationJob.Id
            migrationBuilder.Sql(@"
                UPDATE ""M365Settings"" m
                SET ""ProjectId"" = j.""Id""
                FROM ""MigrationJobs"" j
                WHERE m.""ProjectId"" = j.""ProjectId""
            ");

            migrationBuilder.RenameColumn(
                name: "ProjectId",
                table: "M365Settings",
                newName: "MigrationJobId");

            migrationBuilder.RenameIndex(
                name: "IX_M365Settings_ProjectId",
                table: "M365Settings",
                newName: "IX_M365Settings_MigrationJobId");

            migrationBuilder.RenameColumn(
                name: "ProjectId",
                table: "ImapSettings",
                newName: "MigrationJobId");

            migrationBuilder.RenameIndex(
                name: "IX_ImapSettings_ProjectId",
                table: "ImapSettings",
                newName: "IX_ImapSettings_MigrationJobId");

            migrationBuilder.AddForeignKey(
                name: "FK_ImapSettings_MigrationJobs_MigrationJobId",
                table: "ImapSettings",
                column: "MigrationJobId",
                principalTable: "MigrationJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_M365Settings_MigrationJobs_MigrationJobId",
                table: "M365Settings",
                column: "MigrationJobId",
                principalTable: "MigrationJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImapSettings_MigrationJobs_MigrationJobId",
                table: "ImapSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_M365Settings_MigrationJobs_MigrationJobId",
                table: "M365Settings");

            migrationBuilder.RenameColumn(
                name: "MigrationJobId",
                table: "M365Settings",
                newName: "ProjectId");

            migrationBuilder.RenameIndex(
                name: "IX_M365Settings_MigrationJobId",
                table: "M365Settings",
                newName: "IX_M365Settings_ProjectId");

            migrationBuilder.RenameColumn(
                name: "MigrationJobId",
                table: "ImapSettings",
                newName: "ProjectId");

            migrationBuilder.RenameIndex(
                name: "IX_ImapSettings_MigrationJobId",
                table: "ImapSettings",
                newName: "IX_ImapSettings_ProjectId");

            migrationBuilder.AddColumn<string>(
                name: "EncryptedPassword",
                table: "MigrationJobs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseOwnCredentials",
                table: "MigrationJobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "MigrationJobs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ImapSettings_Projects_ProjectId",
                table: "ImapSettings",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_M365Settings_Projects_ProjectId",
                table: "M365Settings",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
