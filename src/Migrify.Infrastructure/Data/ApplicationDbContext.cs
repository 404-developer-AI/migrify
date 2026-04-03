using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Migrify.Core.Entities;

namespace Migrify.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ImapSettings> ImapSettings => Set<ImapSettings>();
    public DbSet<M365Settings> M365Settings => Set<M365Settings>();
    public DbSet<MigrationJob> MigrationJobs => Set<MigrationJob>();
    public DbSet<ImapProviderPreset> ImapProviderPresets => Set<ImapProviderPreset>();
    public DbSet<GoogleWorkspaceSettings> GoogleWorkspaceSettings => Set<GoogleWorkspaceSettings>();
    public DbSet<DiscoveredMailbox> DiscoveredMailboxes => Set<DiscoveredMailbox>();
    public DbSet<FolderMapping> FolderMappings => Set<FolderMapping>();
    public DbSet<MigrationLog> MigrationLogs => Set<MigrationLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.SourceConnectorType).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("NOW()");
        });

        builder.Entity<MigrationJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceEmail).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DestinationEmail).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("NOW()");

            entity.Property(e => e.MigrationMode).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.CurrentFolder).HasMaxLength(500);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.MigrationJobs)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ImapSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.MigrationJobId).IsUnique();
            entity.Property(e => e.Host).HasMaxLength(255);
            entity.Property(e => e.Username).HasMaxLength(255);
            entity.Property(e => e.EncryptedPassword).HasMaxLength(500);
            entity.Property(e => e.Encryption).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.AuthType).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.LastTestedServerAddress).HasMaxLength(255);
            entity.Property(e => e.ResolvedIpAddress).HasMaxLength(45);

            // OAuth2
            entity.Property(e => e.OAuthClientId).HasMaxLength(255);
            entity.Property(e => e.EncryptedOAuthClientSecret).HasMaxLength(500);
            entity.Property(e => e.EncryptedOAuthAccessToken).HasMaxLength(2000);
            entity.Property(e => e.EncryptedOAuthRefreshToken).HasMaxLength(500);
            entity.Property(e => e.OAuthProvider).HasMaxLength(50);

            entity.HasOne(e => e.MigrationJob)
                .WithOne(j => j.ImapSettings)
                .HasForeignKey<ImapSettings>(e => e.MigrationJobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<M365Settings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProjectId).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(100);
            entity.Property(e => e.ClientId).HasMaxLength(100);
            entity.Property(e => e.EncryptedClientSecret).HasMaxLength(500);

            entity.HasOne(e => e.Project)
                .WithOne(p => p.M365Settings)
                .HasForeignKey<M365Settings>(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GoogleWorkspaceSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProjectId).IsUnique();
            entity.Property(e => e.ServiceAccountEmail).HasMaxLength(255);
            entity.Property(e => e.EncryptedPrivateKey).HasMaxLength(5000);
            entity.Property(e => e.TokenUri).HasMaxLength(500);
            entity.Property(e => e.ImpersonationEmail).HasMaxLength(255);
            entity.Property(e => e.Domain).HasMaxLength(255);

            entity.HasOne(e => e.Project)
                .WithOne(p => p.GoogleWorkspaceSettings)
                .HasForeignKey<GoogleWorkspaceSettings>(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DiscoveredMailbox>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ProjectId, e.Side });
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DisplayName).HasMaxLength(255);
            entity.Property(e => e.Side).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.DiscoveredMailboxes)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FolderMapping>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MigrationJobId, e.SourceFolderName }).IsUnique();
            entity.Property(e => e.SourceFolderName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.DestinationFolderId).IsRequired().HasMaxLength(200);
            entity.Property(e => e.DestinationFolderDisplayName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.MigrationJob)
                .WithMany(j => j.FolderMappings)
                .HasForeignKey(e => e.MigrationJobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MigrationLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Subject).HasMaxLength(500);
            entity.Property(e => e.SourceFolder).HasMaxLength(500);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");

            entity.HasIndex(e => new { e.MigrationJobId, e.Type });
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne(e => e.MigrationJob)
                .WithMany(j => j.MigrationLogs)
                .HasForeignKey(e => e.MigrationJobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ImapProviderPreset>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Pattern, e.MatchType }).IsUnique();
            entity.Property(e => e.Pattern).IsRequired().HasMaxLength(255);
            entity.Property(e => e.MatchType).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Host).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Encryption).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ProviderName).HasMaxLength(100);

            // Seed data — Domain presets
            entity.HasData(
                // Gmail
                Preset("gmail.com", "imap.gmail.com", "Gmail"),
                Preset("googlemail.com", "imap.gmail.com", "Gmail"),
                // Microsoft
                Preset("outlook.com", "outlook.office365.com", "Outlook.com"),
                Preset("hotmail.com", "outlook.office365.com", "Outlook.com"),
                Preset("live.com", "outlook.office365.com", "Outlook.com"),
                Preset("msn.com", "outlook.office365.com", "Outlook.com"),
                // Yahoo
                Preset("yahoo.com", "imap.mail.yahoo.com", "Yahoo"),
                Preset("yahoo.co.uk", "imap.mail.yahoo.com", "Yahoo"),
                Preset("yahoo.fr", "imap.mail.yahoo.com", "Yahoo"),
                Preset("yahoo.de", "imap.mail.yahoo.com", "Yahoo"),
                Preset("yahoo.nl", "imap.mail.yahoo.com", "Yahoo"),
                // Apple
                Preset("icloud.com", "imap.mail.me.com", "iCloud"),
                Preset("me.com", "imap.mail.me.com", "iCloud"),
                Preset("mac.com", "imap.mail.me.com", "iCloud"),
                // one.com
                Preset("one.com", "imap.one.com", "one.com"),
                // Zoho
                Preset("zoho.com", "imap.zoho.com", "Zoho"),
                Preset("zohomail.eu", "imap.zoho.eu", "Zoho"),
                // FastMail
                Preset("fastmail.com", "imap.fastmail.com", "FastMail"),
                // ProtonMail Bridge
                Preset("protonmail.com", "127.0.0.1", "ProtonMail Bridge", 1143, ImapEncryption.STARTTLS),
                Preset("proton.me", "127.0.0.1", "ProtonMail Bridge", 1143, ImapEncryption.STARTTLS),
                // GMX
                Preset("gmx.com", "imap.gmx.net", "GMX"),
                Preset("gmx.de", "imap.gmx.net", "GMX"),
                Preset("gmx.net", "imap.gmx.net", "GMX"),
                // Mail.de
                Preset("mail.de", "imap.mail.de", "Mail.de"),
                // AOL
                Preset("aol.com", "imap.aol.com", "AOL"),
                // T-Online
                Preset("t-online.de", "secureimap.t-online.de", "T-Online"),
                // Web.de
                Preset("web.de", "imap.web.de", "Web.de"),
                // Telenet
                Preset("telenet.be", "imap.telenet.be", "Telenet"),
                // Ziggo
                Preset("ziggo.nl", "imap.ziggo.nl", "Ziggo"),
                // KPN
                Preset("kpnmail.nl", "imap.kpnmail.nl", "KPN"),

                // Seed data — MX pattern presets
                MxPreset("protection.outlook.com", "outlook.office365.com", "Microsoft 365"),
                MxPreset("outlook.com", "outlook.office365.com", "Microsoft 365"),
                MxPreset("google.com", "imap.gmail.com", "Google Workspace"),
                MxPreset("googlemail.com", "imap.gmail.com", "Google Workspace"),
                MxPreset("yahoodns.net", "imap.mail.yahoo.com", "Yahoo"),
                MxPreset("zoho.com", "imap.zoho.com", "Zoho"),
                MxPreset("zoho.eu", "imap.zoho.eu", "Zoho"),
                MxPreset("one.com", "imap.one.com", "one.com"),
                MxPreset("fastmail.com", "imap.fastmail.com", "FastMail")
            );
        });
    }

    // Deterministic GUIDs from pattern + matchtype so seed data is stable across migrations
    private static Guid DeterministicGuid(string input)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(bytes);
    }

    private static ImapProviderPreset Preset(string pattern, string host, string provider,
        int port = 993, ImapEncryption encryption = ImapEncryption.SSL) => new()
    {
        Id = DeterministicGuid($"Domain:{pattern}"),
        Pattern = pattern,
        MatchType = ProviderMatchType.Domain,
        Host = host,
        Port = port,
        Encryption = encryption,
        ProviderName = provider
    };

    private static ImapProviderPreset MxPreset(string pattern, string host, string provider,
        int port = 993, ImapEncryption encryption = ImapEncryption.SSL) => new()
    {
        Id = DeterministicGuid($"MxPattern:{pattern}"),
        Pattern = pattern,
        MatchType = ProviderMatchType.MxPattern,
        Host = host,
        Port = port,
        Encryption = encryption,
        ProviderName = provider
    };
}
