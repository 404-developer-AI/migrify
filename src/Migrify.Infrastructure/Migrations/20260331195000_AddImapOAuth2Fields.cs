using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Migrify.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImapOAuth2Fields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptedOAuthAccessToken",
                table: "ImapSettings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedOAuthClientSecret",
                table: "ImapSettings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedOAuthRefreshToken",
                table: "ImapSettings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthClientId",
                table: "ImapSettings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthProvider",
                table: "ImapSettings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OAuthTokenExpiresAtUtc",
                table: "ImapSettings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptedOAuthAccessToken",
                table: "ImapSettings");

            migrationBuilder.DropColumn(
                name: "EncryptedOAuthClientSecret",
                table: "ImapSettings");

            migrationBuilder.DropColumn(
                name: "EncryptedOAuthRefreshToken",
                table: "ImapSettings");

            migrationBuilder.DropColumn(
                name: "OAuthClientId",
                table: "ImapSettings");

            migrationBuilder.DropColumn(
                name: "OAuthProvider",
                table: "ImapSettings");

            migrationBuilder.DropColumn(
                name: "OAuthTokenExpiresAtUtc",
                table: "ImapSettings");
        }
    }
}
