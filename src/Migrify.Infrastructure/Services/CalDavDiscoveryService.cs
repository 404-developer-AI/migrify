using System.Net;
using DnsClient;
using Microsoft.Extensions.Logging;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;
using Migrify.Core.Models;

namespace Migrify.Infrastructure.Services;

public class CalDavDiscoveryService : ICalDavDiscoveryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CalDavDiscoveryService> _logger;

    // Known CalDAV provider presets (hardcoded — no DB table needed)
    private static readonly Dictionary<string, (string BaseUrl, string ProviderName)> DomainPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fastmail.com"] = ("https://caldav.fastmail.com/dav/principals/user/{user}/", "Fastmail"),
        ["messagingengine.com"] = ("https://caldav.fastmail.com/dav/principals/user/{user}/", "Fastmail"),
        ["yahoo.com"] = ("https://caldav.calendar.yahoo.com/", "Yahoo"),
        ["yahoo.co.uk"] = ("https://caldav.calendar.yahoo.com/", "Yahoo"),
        ["gmx.com"] = ("https://caldav.gmx.com/", "GMX"),
        ["gmx.net"] = ("https://caldav.gmx.net/", "GMX"),
        ["gmx.de"] = ("https://caldav.gmx.net/", "GMX"),
        ["web.de"] = ("https://caldav.web.de/", "Web.de"),
        ["mailbox.org"] = ("https://dav.mailbox.org/caldav/", "Mailbox.org"),
        ["posteo.de"] = ("https://posteo.de:8843/calendars/{user}/", "Posteo"),
        ["posteo.net"] = ("https://posteo.de:8843/calendars/{user}/", "Posteo"),
        ["zoho.com"] = ("https://calendar.zoho.com/caldav/", "Zoho"),
    };

    // MX patterns for providers that host multiple domains
    private static readonly (string MxPattern, string BaseUrl, string ProviderName)[] MxPresets =
    [
        ("messagingengine.com", "https://caldav.fastmail.com/dav/principals/user/{user}/", "Fastmail"),
        ("yahoodns.net", "https://caldav.calendar.yahoo.com/", "Yahoo"),
        ("gmx.net", "https://caldav.gmx.net/", "GMX"),
    ];

    // Domains known to NOT support CalDAV (use their own API instead)
    private static readonly HashSet<string> NoCalDavDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com",
        "msn.com", "icloud.com", "me.com", "mac.com"
    };

    public CalDavDiscoveryService(IHttpClientFactory httpClientFactory, ILogger<CalDavDiscoveryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CalDavDiscoveryResult> DiscoverAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        var atIndex = emailAddress.IndexOf('@');
        if (atIndex < 0 || atIndex >= emailAddress.Length - 1)
            return new CalDavDiscoveryResult(CalDavSupportStatus.NotSupported, null, null);

        var domain = emailAddress[(atIndex + 1)..].Trim().ToLowerInvariant();

        _logger.LogInformation("CalDAV discovery starting for domain {Domain}", domain);

        // Step 0: Known non-CalDAV providers
        if (NoCalDavDomains.Contains(domain))
        {
            _logger.LogInformation("Domain {Domain} is a known non-CalDAV provider", domain);
            return new CalDavDiscoveryResult(CalDavSupportStatus.NotSupported, null, null);
        }

        // Step 1: Domain preset lookup
        if (DomainPresets.TryGetValue(domain, out var preset))
        {
            var url = preset.BaseUrl.Replace("{user}", emailAddress);
            _logger.LogInformation("CalDAV preset found for {Domain}: {Provider}", domain, preset.ProviderName);
            return new CalDavDiscoveryResult(CalDavSupportStatus.Supported, url, preset.ProviderName);
        }

        // Step 2: Well-known URL discovery (RFC 5785)
        var wellKnownResult = await TryWellKnownAsync(domain, cancellationToken);
        if (wellKnownResult is not null)
            return wellKnownResult;

        // Step 3: SRV record lookup
        var srvResult = await TrySrvRecordsAsync(domain, cancellationToken);
        if (srvResult is not null)
            return srvResult;

        // Step 4: MX pattern matching
        var mxResult = await TryMxPatternsAsync(domain, cancellationToken);
        if (mxResult is not null)
            return mxResult;

        _logger.LogInformation("CalDAV discovery found no CalDAV support for {Domain}", domain);
        return new CalDavDiscoveryResult(CalDavSupportStatus.NotSupported, null, null);
    }

    private async Task<CalDavDiscoveryResult?> TryWellKnownAsync(string domain, CancellationToken cancellationToken)
    {
        foreach (var scheme in new[] { "https", "http" })
        {
            try
            {
                var client = _httpClientFactory.CreateClient("CalDavDiscovery");
                var url = $"{scheme}://{domain}/.well-known/caldav";

                _logger.LogDebug("Trying well-known URL: {Url}", url);

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                // 301/302 redirect → the server knows about CalDAV
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var location = response.Headers.Location?.ToString();
                    if (!string.IsNullOrEmpty(location))
                    {
                        // Make absolute if relative
                        if (!location.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            location = $"{scheme}://{domain}{location}";

                        _logger.LogInformation("CalDAV well-known redirect found: {Location}", location);
                        return new CalDavDiscoveryResult(CalDavSupportStatus.Supported, location, null);
                    }
                }

                // 200 OK → the well-known URL itself is the CalDAV root
                if (response.IsSuccessStatusCode)
                {
                    var baseUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
                    _logger.LogInformation("CalDAV well-known URL responded successfully: {Url}", baseUrl);
                    return new CalDavDiscoveryResult(CalDavSupportStatus.Supported, baseUrl, null);
                }

                // 401/403 → server speaks CalDAV but wants auth
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    _logger.LogInformation("CalDAV well-known returned {Status} — server supports CalDAV", response.StatusCode);
                    return new CalDavDiscoveryResult(CalDavSupportStatus.Supported, $"{scheme}://{domain}/", null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Well-known CalDAV check failed for {Scheme}://{Domain}", scheme, domain);
            }
        }

        return null;
    }

    private async Task<CalDavDiscoveryResult?> TrySrvRecordsAsync(string domain, CancellationToken cancellationToken)
    {
        try
        {
            var lookup = new LookupClient();

            // Try HTTPS first (_caldavs), then HTTP (_caldav)
            foreach (var (srvName, scheme) in new[] { ($"_caldavs._tcp.{domain}", "https"), ($"_caldav._tcp.{domain}", "http") })
            {
                var result = await lookup.QueryAsync(srvName, QueryType.SRV, cancellationToken: cancellationToken);
                var srvRecords = result.Answers.SrvRecords().ToList();

                if (srvRecords.Count > 0)
                {
                    var srv = srvRecords.OrderBy(s => s.Priority).ThenBy(s => s.Weight).First();
                    var host = srv.Target.Value.TrimEnd('.');
                    var port = srv.Port;

                    var baseUrl = port == 443 || port == 80
                        ? $"{scheme}://{host}/"
                        : $"{scheme}://{host}:{port}/";

                    _logger.LogInformation("CalDAV SRV record found: {SrvName} → {BaseUrl}", srvName, baseUrl);
                    return new CalDavDiscoveryResult(CalDavSupportStatus.Supported, baseUrl, null);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SRV record lookup failed for {Domain}", domain);
        }

        return null;
    }

    private async Task<CalDavDiscoveryResult?> TryMxPatternsAsync(string domain, CancellationToken cancellationToken)
    {
        try
        {
            var lookup = new LookupClient();
            var result = await lookup.QueryAsync(domain, QueryType.MX, cancellationToken: cancellationToken);
            var mxRecords = result.Answers.MxRecords().ToList();

            if (mxRecords.Count == 0)
                return null;

            foreach (var mx in mxRecords.OrderBy(m => m.Preference))
            {
                var exchange = mx.Exchange.Value.ToLowerInvariant();

                foreach (var (pattern, baseUrl, providerName) in MxPresets)
                {
                    if (exchange.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("CalDAV MX pattern match: {Exchange} matches {Pattern} ({Provider})", exchange, pattern, providerName);
                        return new CalDavDiscoveryResult(CalDavSupportStatus.Supported, baseUrl, providerName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MX pattern lookup failed for {Domain}", domain);
        }

        return null;
    }
}
