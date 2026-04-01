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

        return services;
    }
}
