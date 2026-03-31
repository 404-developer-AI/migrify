using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Migrify.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelenetPreset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ImapProviderPresets",
                columns: new[] { "Id", "Encryption", "Host", "MatchType", "Pattern", "Port", "ProviderName" },
                values: new object[] { new Guid("6f3d34de-e7e5-f7cb-30fc-8ce4464c8dcf"), "SSL", "imap.telenet.be", "Domain", "telenet.be", 993, "Telenet" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ImapProviderPresets",
                keyColumn: "Id",
                keyValue: new Guid("6f3d34de-e7e5-f7cb-30fc-8ce4464c8dcf"));
        }
    }
}
