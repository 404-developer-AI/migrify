using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Migrify.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRetryResumeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DestinationFolderId",
                table: "MigrationLogs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternetMessageId",
                table: "MigrationLogs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceUid",
                table: "MigrationLogs",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MigrationLogs_MigrationJobId_InternetMessageId",
                table: "MigrationLogs",
                columns: new[] { "MigrationJobId", "InternetMessageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MigrationLogs_MigrationJobId_InternetMessageId",
                table: "MigrationLogs");

            migrationBuilder.DropColumn(
                name: "DestinationFolderId",
                table: "MigrationLogs");

            migrationBuilder.DropColumn(
                name: "InternetMessageId",
                table: "MigrationLogs");

            migrationBuilder.DropColumn(
                name: "SourceUid",
                table: "MigrationLogs");
        }
    }
}
