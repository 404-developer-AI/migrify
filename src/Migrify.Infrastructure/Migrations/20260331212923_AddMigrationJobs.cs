using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Migrify.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DestinationEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UseOwnCredentials = table.Column<bool>(type: "boolean", nullable: false),
                    Username = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    EncryptedPassword = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationJobs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationJobs_ProjectId",
                table: "MigrationJobs",
                column: "ProjectId");

            // Backward compatibility: create a MigrationJob for each existing project with IMAP username
            migrationBuilder.Sql(@"
                INSERT INTO ""MigrationJobs"" (""Id"", ""ProjectId"", ""SourceEmail"", ""DestinationEmail"", ""Status"", ""UseOwnCredentials"", ""CreatedAt"", ""UpdatedAt"")
                SELECT gen_random_uuid(), i.""ProjectId"", i.""Username"", '', 'New', false, NOW(), NOW()
                FROM ""ImapSettings"" i
                WHERE i.""Username"" IS NOT NULL AND i.""Username"" <> ''
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationJobs");
        }
    }
}
