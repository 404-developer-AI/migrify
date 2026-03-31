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
        services.AddTransient<IImapConnectionTester, ImapConnectionTester>();
        services.AddTransient<IM365ConnectionTester, M365ConnectionTester>();

        services.AddHttpClient("ImapAutoDiscovery", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddTransient<IImapAutoDiscoveryService, ImapAutoDiscoveryService>();

        return services;
    }
}
