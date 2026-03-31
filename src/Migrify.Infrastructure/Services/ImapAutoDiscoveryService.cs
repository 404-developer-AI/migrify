using System.Xml.Linq;
using DnsClient;
using Microsoft.EntityFrameworkCore;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;
using Migrify.Core.Models;
using Migrify.Infrastructure.Data;

namespace Migrify.Infrastructure.Services;

public class ImapAutoDiscoveryService : IImapAutoDiscoveryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApplicationDbContext _db;

    public ImapAutoDiscoveryService(IHttpClientFactory httpClientFactory, ApplicationDbContext db)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
    }

    public async Task<ImapAutoDiscoveryResult?> DiscoverAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        var atIndex = emailAddress.IndexOf('@');
        if (atIndex < 0 || atIndex >= emailAddress.Length - 1)
            return null;

        var domain = emailAddress[(atIndex + 1)..].Trim().ToLowerInvariant();

        // Step 1: Domain preset lookup from database
        var domainPreset = await _db.ImapProviderPresets
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.MatchType == ProviderMatchType.Domain && p.Pattern == domain, cancellationToken);

        if (domainPreset is not null)
            return ToResult(domainPreset);

        // Step 2: Mozilla ISPDB fallback
        var ispdbResult = await QueryMozillaIspDbAsync(domain, cancellationToken);
        if (ispdbResult is not null)
            return ispdbResult;

        // Step 3: MX record lookup against database patterns
        return await QueryMxRecordsAsync(domain, cancellationToken);
    }

    private async Task<ImapAutoDiscoveryResult?> QueryMozillaIspDbAsync(string domain, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ImapAutoDiscovery");

            var response = await client.GetAsync(
                $"https://autoconfig.thunderbird.net/v1.1/{domain}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = XDocument.Parse(xml);

            var imapServer = doc.Descendants("incomingServer")
                .FirstOrDefault(e => e.Attribute("type")?.Value == "imap");

            if (imapServer is null)
                return null;

            var host = imapServer.Element("hostname")?.Value;
            var portStr = imapServer.Element("port")?.Value;
            var socketType = imapServer.Element("socketType")?.Value;

            if (string.IsNullOrWhiteSpace(host) || !int.TryParse(portStr, out var port))
                return null;

            var encryption = socketType?.ToUpperInvariant() switch
            {
                "SSL" => ImapEncryption.SSL,
                "STARTTLS" => ImapEncryption.STARTTLS,
                "PLAIN" => ImapEncryption.None,
                _ => ImapEncryption.SSL
            };

            return new ImapAutoDiscoveryResult(host, port, encryption);
        }
        catch
        {
            return null;
        }
    }

    private async Task<ImapAutoDiscoveryResult?> QueryMxRecordsAsync(string domain, CancellationToken cancellationToken)
    {
        try
        {
            var lookup = new LookupClient();
            var result = await lookup.QueryAsync(domain, QueryType.MX, cancellationToken: cancellationToken);

            var mxRecords = result.Answers.MxRecords().ToList();
            if (mxRecords.Count == 0)
                return null;

            var mxPatterns = await _db.ImapProviderPresets
                .AsNoTracking()
                .Where(p => p.MatchType == ProviderMatchType.MxPattern)
                .ToListAsync(cancellationToken);

            foreach (var mx in mxRecords.OrderBy(m => m.Preference))
            {
                var exchange = mx.Exchange.Value.ToLowerInvariant();

                foreach (var preset in mxPatterns)
                {
                    if (exchange.Contains(preset.Pattern, StringComparison.OrdinalIgnoreCase))
                        return ToResult(preset);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static ImapAutoDiscoveryResult ToResult(ImapProviderPreset preset) =>
        new(preset.Host, preset.Port, preset.Encryption, preset.ProviderName);
}
