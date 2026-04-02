using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Migrify.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateFrom",
                table: "MigrationJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateTo",
                table: "MigrationJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MigrationMode",
                table: "MigrationJobs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "MigrationOptionsConfigured",
                table: "MigrationJobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SkipDuplicates",
                table: "MigrationJobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SkippedMessages",
                table: "MigrationJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateFrom",
                table: "MigrationJobs");

            migrationBuilder.DropColumn(
                name: "DateTo",
                table: "MigrationJobs");

            migrationBuilder.DropColumn(
                name: "MigrationMode",
                table: "MigrationJobs");

            migrationBuilder.DropColumn(
                name: "MigrationOptionsConfigured",
                table: "MigrationJobs");

            migrationBuilder.DropColumn(
                name: "SkipDuplicates",
                table: "MigrationJobs");

            migrationBuilder.DropColumn(
                name: "SkippedMessages",
                table: "MigrationJobs");
        }
    }
}
