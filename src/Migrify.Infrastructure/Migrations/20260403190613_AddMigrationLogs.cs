using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Migrify.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MigrationJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MessageDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceFolder = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TotalProcessed = table.Column<int>(type: "integer", nullable: true),
                    TotalFailed = table.Column<int>(type: "integer", nullable: true),
                    TotalSkipped = table.Column<int>(type: "integer", nullable: true),
                    TotalDuplicates = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationLogs_MigrationJobs_MigrationJobId",
                        column: x => x.MigrationJobId,
                        principalTable: "MigrationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationLogs_CreatedAt",
                table: "MigrationLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MigrationLogs_MigrationJobId_Type",
                table: "MigrationLogs",
                columns: new[] { "MigrationJobId", "Type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationLogs");
        }
    }
}
