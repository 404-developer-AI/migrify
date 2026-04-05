using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Migrify.Core.Interfaces;
using Migrify.Infrastructure.Data;
using Migrify.Infrastructure.Security;
using Migrify.Infrastructure.Services;

namespace Migrify.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddSingleton<ICredentialEncryptor, AesCredentialEncryptor>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IMigrationJobRepository, MigrationJobRepository>();
        services.AddTransient<IImapConnectionTester, ImapConnectionTester>();
        services.AddTransient<IImapMailboxExplorer, ImapMailboxExplorer>();
        services.AddTransient<IM365ConnectionTester, M365ConnectionTester>();
        services.AddTransient<IM365MailboxExplorer, M365MailboxExplorer>();
        services.AddTransient<IM365MailboxDiscovery, M365MailboxDiscovery>();
        services.AddTransient<IGoogleWorkspaceMailboxDiscovery, GoogleWorkspaceMailboxDiscovery>();
        services.AddScoped<IDiscoveredMailboxRepository, DiscoveredMailboxRepository>();
        services.AddScoped<IFolderMappingRepository, FolderMappingRepository>();
        services.AddScoped<IMigrationLogRepository, MigrationLogRepository>();

        services.AddHttpClient("ImapAutoDiscovery", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddTransient<IImapAutoDiscoveryService, ImapAutoDiscoveryService>();

        // OAuth2
        services.AddHttpClient("GoogleOAuth", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddTransient<IOAuthTokenService, GoogleOAuthTokenService>();
        services.AddScoped<IImapOAuthCredentialProvider, ImapOAuthCredentialProvider>();

        // Migration engine & retry
        services.AddTransient<IMigrationRetryService, MigrationRetryService>();
        services.AddSingleton<MigrationQueueService>();
        services.AddSingleton<IMigrationQueueService>(sp => sp.GetRequiredService<MigrationQueueService>());
        services.AddTransient<IMigrationEngine, MigrationEngine>();
        services.AddSingleton<MigrationBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<MigrationBackgroundService>());

        // CalDAV discovery & explore
        services.AddHttpClient("CalDavDiscovery", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false, // We handle redirects manually for well-known discovery
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });
        services.AddHttpClient("CalDav", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        });
        services.AddTransient<ICalDavDiscoveryService, CalDavDiscoveryService>();
        services.AddTransient<ICalDavExplorer, CalDavExplorer>();

        // Concurrency limit services
        services.AddSingleton<SystemResourceMonitor>();
        services.AddSingleton<SourceLimitProvider>();
        services.AddSingleton<DestinationLimitProvider>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IConcurrencyLimitService, ConcurrencyLimitService>();

        return services;
    }
}
