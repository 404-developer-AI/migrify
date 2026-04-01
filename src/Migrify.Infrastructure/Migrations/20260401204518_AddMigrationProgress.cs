using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Migrify.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentFolder",
                table: "MigrationJobs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "MigrationJobs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessedMessages",
                table: "MigrationJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalMessages",
                table: "MigrationJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentFolder",
                table: "MigrationJobs");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "MigrationJobs");

            migrationBuilder.DropColumn(
                name: "ProcessedMessages",
                table: "MigrationJobs");

            migrationBuilder.DropColumn(
                name: "TotalMessages",
                table: "MigrationJobs");
        }
    }
}
