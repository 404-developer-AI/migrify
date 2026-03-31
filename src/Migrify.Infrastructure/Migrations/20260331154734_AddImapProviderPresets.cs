using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Migrify.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImapProviderPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImapProviderPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Pattern = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MatchType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Encryption = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImapProviderPresets", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ImapProviderPresets",
                columns: new[] { "Id", "Encryption", "Host", "MatchType", "Pattern", "Port", "ProviderName" },
                values: new object[,]
                {
                    { new Guid("048a30ce-e61a-d0cc-e52a-5f9f3cdb543b"), "SSL", "outlook.office365.com", "Domain", "hotmail.com", 993, "Outlook.com" },
                    { new Guid("09902a57-7f7b-5d7e-df3b-463dab159daf"), "SSL", "imap.kpnmail.nl", "Domain", "kpnmail.nl", 993, "KPN" },
                    { new Guid("0a4a4344-8a14-3b90-b717-0dec68e3f8e1"), "SSL", "imap.one.com", "Domain", "one.com", 993, "one.com" },
                    { new Guid("14b2d807-4b88-397c-b006-3f74fa32af45"), "SSL", "imap.gmx.net", "Domain", "gmx.de", 993, "GMX" },
                    { new Guid("17f02716-379a-cd5b-0b50-2db6a975bb03"), "SSL", "imap.mail.me.com", "Domain", "me.com", 993, "iCloud" },
                    { new Guid("224e8779-1908-9eae-31f7-e4d66fae6c5b"), "SSL", "imap.gmx.net", "Domain", "gmx.net", 993, "GMX" },
                    { new Guid("286dc0ea-621d-aa0b-bf0d-c673ac25cf38"), "SSL", "imap.one.com", "MxPattern", "one.com", 993, "one.com" },
                    { new Guid("28b2c5cb-7034-3343-18fa-5b883650f2c3"), "SSL", "imap.zoho.com", "MxPattern", "zoho.com", 993, "Zoho" },
                    { new Guid("2e629e54-58f8-4e77-9b38-ec0012d7e3f9"), "SSL", "imap.gmail.com", "Domain", "gmail.com", 993, "Gmail" },
                    { new Guid("335bface-248c-0a5c-62cf-96adf66ad1a6"), "SSL", "imap.mail.yahoo.com", "Domain", "yahoo.com", 993, "Yahoo" },
                    { new Guid("4d9d54fe-69ed-715e-f660-283fd2c2a7bf"), "SSL", "imap.zoho.com", "Domain", "zoho.com", 993, "Zoho" },
                    { new Guid("52c2ff8b-7e49-c0c2-01d2-d02d0475ba84"), "SSL", "imap.mail.yahoo.com", "MxPattern", "yahoodns.net", 993, "Yahoo" },
                    { new Guid("5de50aa5-b29d-05a1-4a0b-e1c14d3c8f2b"), "SSL", "imap.zoho.eu", "Domain", "zohomail.eu", 993, "Zoho" },
                    { new Guid("6b17e395-e71d-2963-5d88-719de03e9014"), "SSL", "outlook.office365.com", "Domain", "live.com", 993, "Outlook.com" },
                    { new Guid("6bae2a0e-e388-4fec-b165-e9a7c20a7c8e"), "SSL", "secureimap.t-online.de", "Domain", "t-online.de", 993, "T-Online" },
                    { new Guid("6d86e923-9c4e-ffda-55a3-689c1973eff2"), "SSL", "outlook.office365.com", "Domain", "msn.com", 993, "Outlook.com" },
                    { new Guid("6ff760cd-4874-f8f6-9185-e6aaa01ce0ce"), "SSL", "imap.mail.yahoo.com", "Domain", "yahoo.de", 993, "Yahoo" },
                    { new Guid("75e3c102-5e5c-b8ee-a555-385050fa19d1"), "SSL", "outlook.office365.com", "MxPattern", "outlook.com", 993, "Microsoft 365" },
                    { new Guid("855630c8-45bd-eb28-26da-85c15bdb1c51"), "SSL", "imap.aol.com", "Domain", "aol.com", 993, "AOL" },
                    { new Guid("87b2a7bc-ce72-4ab8-5cd8-09a5bbb5a5c4"), "SSL", "imap.mail.de", "Domain", "mail.de", 993, "Mail.de" },
                    { new Guid("8f1bc95a-c068-1774-ece1-710e450c867b"), "SSL", "imap.mail.yahoo.com", "Domain", "yahoo.fr", 993, "Yahoo" },
                    { new Guid("9496877d-2676-81e1-81cd-1d24cc96e3f4"), "SSL", "imap.zoho.eu", "MxPattern", "zoho.eu", 993, "Zoho" },
                    { new Guid("9e17a8fb-ca3f-df13-9a3c-c94df6748aff"), "SSL", "imap.web.de", "Domain", "web.de", 993, "Web.de" },
                    { new Guid("a0f1ae43-1e97-5fd5-93d7-8f8d26276a03"), "SSL", "outlook.office365.com", "MxPattern", "protection.outlook.com", 993, "Microsoft 365" },
                    { new Guid("acb15a15-c727-97a4-e981-1d73c7038b9d"), "SSL", "imap.mail.yahoo.com", "Domain", "yahoo.co.uk", 993, "Yahoo" },
                    { new Guid("ae42cb34-55c2-b1c2-da7f-dd2b196efa44"), "STARTTLS", "127.0.0.1", "Domain", "protonmail.com", 1143, "ProtonMail Bridge" },
                    { new Guid("b13e9a8c-f273-f6f0-dbe1-92c7113b09b6"), "SSL", "imap.fastmail.com", "MxPattern", "fastmail.com", 993, "FastMail" },
                    { new Guid("b54b68cf-6ebf-f888-6d5b-83b09d812b26"), "SSL", "imap.mail.yahoo.com", "Domain", "yahoo.nl", 993, "Yahoo" },
                    { new Guid("bf62f05f-8dca-89fb-36a5-06c2efc72bd1"), "SSL", "imap.mail.me.com", "Domain", "icloud.com", 993, "iCloud" },
                    { new Guid("c09cd863-0832-b11a-4f90-167bbec450d5"), "SSL", "outlook.office365.com", "Domain", "outlook.com", 993, "Outlook.com" },
                    { new Guid("c69b93fb-5232-47ae-388a-99fe9024b9d2"), "SSL", "imap.fastmail.com", "Domain", "fastmail.com", 993, "FastMail" },
                    { new Guid("c71f0920-cce8-930f-b4e5-0ecfc71aab0e"), "SSL", "imap.gmail.com", "Domain", "googlemail.com", 993, "Gmail" },
                    { new Guid("cecb6a7e-3f78-9208-6c3c-5d2d87147164"), "SSL", "imap.gmail.com", "MxPattern", "googlemail.com", 993, "Google Workspace" },
                    { new Guid("dacacc40-6f81-7d95-b111-2d8247927d60"), "SSL", "imap.gmail.com", "MxPattern", "google.com", 993, "Google Workspace" },
                    { new Guid("e6981d4c-f118-4105-88b5-d07639931ff3"), "SSL", "imap.gmx.net", "Domain", "gmx.com", 993, "GMX" },
                    { new Guid("f07c9222-6d92-c435-868f-7e37be9cde10"), "SSL", "imap.ziggo.nl", "Domain", "ziggo.nl", 993, "Ziggo" },
                    { new Guid("f0fbac11-58fc-2369-55c5-c769138939fb"), "SSL", "imap.mail.me.com", "Domain", "mac.com", 993, "iCloud" },
                    { new Guid("f5421ae2-6be2-dc13-c955-3a62d725920e"), "STARTTLS", "127.0.0.1", "Domain", "proton.me", 1143, "ProtonMail Bridge" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImapProviderPresets_Pattern_MatchType",
                table: "ImapProviderPresets",
                columns: new[] { "Pattern", "MatchType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImapProviderPresets");
        }
    }
}
