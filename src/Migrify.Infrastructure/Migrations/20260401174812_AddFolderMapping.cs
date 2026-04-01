using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Migrify.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FolderMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MigrationJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceFolderName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DestinationFolderId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DestinationFolderDisplayName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FolderMappings_MigrationJobs_MigrationJobId",
                        column: x => x.MigrationJobId,
                        principalTable: "MigrationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FolderMappings_MigrationJobId_SourceFolderName",
                table: "FolderMappings",
                columns: new[] { "MigrationJobId", "SourceFolderName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FolderMappings");
        }
    }
}
