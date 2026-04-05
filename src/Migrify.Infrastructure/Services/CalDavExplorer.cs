using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Migrify.Core.Interfaces;
using Migrify.Core.Models;

namespace Migrify.Infrastructure.Services;

public class CalDavExplorer : ICalDavExplorer
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CalDavExplorer> _logger;

    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";
    private static readonly XNamespace AppleCal = "http://apple.com/ns/ical/";
    private static readonly XNamespace CalServer = "http://calendarserver.org/ns/";

    public CalDavExplorer(IHttpClientFactory httpClientFactory, ILogger<CalDavExplorer> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CalDavExploreResult> ExploreAsync(
        string baseUrl, string username, string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("CalDav");
            var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);

            _logger.LogInformation("CalDAV explore starting for {Username} at {BaseUrl}", username, baseUrl);

            // Strategy 1: Standard CalDAV discovery (current-user-principal → calendar-home-set → calendars)
            var calendars = await TryStandardDiscoveryAsync(client, baseUrl, username, cancellationToken);
            if (calendars is not null)
                return new CalDavExploreResult(true, null, calendars);

            // Strategy 2: Try well-known CalDAV paths with username variations
            calendars = await TryCommonPathsAsync(client, baseUrl, username, cancellationToken);
            if (calendars is not null)
                return new CalDavExploreResult(true, null, calendars);

            // Strategy 3: Direct PROPFIND on base URL with Depth:1 (some servers expose calendars directly)
            calendars = await TryDirectListingAsync(client, baseUrl, cancellationToken);
            if (calendars is not null)
                return new CalDavExploreResult(true, null, calendars);

            _logger.LogWarning("All CalDAV explore strategies failed for {BaseUrl}", baseUrl);
            return new CalDavExploreResult(false, "Could not discover calendars. The server may use a non-standard CalDAV layout, or the credentials are incorrect.", []);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error during CalDAV explore");
            var message = ex.StatusCode == HttpStatusCode.Unauthorized
                ? "Authentication failed. Check your credentials."
                : $"Connection failed: {ex.Message}";
            return new CalDavExploreResult(false, message, []);
        }
        catch (TaskCanceledException)
        {
            return new CalDavExploreResult(false, "Connection timed out. The CalDAV server did not respond in time.", []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during CalDAV explore");
            return new CalDavExploreResult(false, $"Unexpected error: {ex.Message}", []);
        }
    }

    /// <summary>
    /// Standard RFC 4791 discovery: PROPFIND for current-user-principal → calendar-home-set → list calendars.
    /// </summary>
    private async Task<List<CalDavCalendarInfo>?> TryStandardDiscoveryAsync(HttpClient client, string baseUrl, string username, CancellationToken ct)
    {
        _logger.LogInformation("Strategy 1: Standard CalDAV discovery at {BaseUrl}", baseUrl);

        // Ask for both current-user-principal and calendar-home-set in one request
        var principalUrl = await FindCurrentUserPrincipalAsync(client, baseUrl, ct);
        if (principalUrl is null)
        {
            _logger.LogInformation("No current-user-principal found at {BaseUrl}, trying calendar-home-set directly", baseUrl);

            // Some servers skip the principal and expose calendar-home-set directly
            var calendarHomeUrl = await FindCalendarHomeSetAsync(client, baseUrl, ct);
            if (calendarHomeUrl is not null)
            {
                calendarHomeUrl = MakeAbsolute(baseUrl, calendarHomeUrl);
                _logger.LogInformation("Found calendar-home-set directly: {Url}", calendarHomeUrl);
                return await ListCalendarsAsync(client, calendarHomeUrl, ct);
            }

            return null;
        }

        principalUrl = MakeAbsolute(baseUrl, principalUrl);
        _logger.LogInformation("Found principal: {PrincipalUrl}", principalUrl);

        var homeUrl = await FindCalendarHomeSetAsync(client, principalUrl, ct);
        if (homeUrl is null)
        {
            _logger.LogInformation("No calendar-home-set at principal {Url}", principalUrl);
            return null;
        }

        homeUrl = MakeAbsolute(baseUrl, homeUrl);
        _logger.LogInformation("Found calendar home: {Url}", homeUrl);
        return await ListCalendarsAsync(client, homeUrl, ct);
    }

    /// <summary>
    /// Try common CalDAV URL patterns used by various servers.
    /// </summary>
    private async Task<List<CalDavCalendarInfo>?> TryCommonPathsAsync(HttpClient client, string baseUrl, string username, CancellationToken ct)
    {
        var baseUri = new Uri(baseUrl);
        var origin = $"{baseUri.Scheme}://{baseUri.Authority}";
        var localPart = username.Contains('@') ? username[..username.IndexOf('@')] : username;

        var pathsToTry = new[]
        {
            // Common CalDAV paths used by various servers
            $"{origin}/caldav/{username}/",
            $"{origin}/caldav/{localPart}/",
            $"{origin}/dav/{username}/",
            $"{origin}/dav/calendars/user/{username}/",
            $"{origin}/dav/calendars/user/{localPart}/",
            $"{origin}/calendars/{username}/",
            $"{origin}/calendars/{localPart}/",
            $"{origin}/remote.php/dav/calendars/{username}/",      // Nextcloud
            $"{origin}/remote.php/dav/calendars/{localPart}/",     // Nextcloud
            $"{origin}/SOGo/dav/{username}/Calendar/",              // SOGo
            $"{origin}/SOGo/dav/{localPart}/Calendar/",             // SOGo
            $"{origin}/users/{username}/calendars/",
        };

        _logger.LogInformation("Strategy 2: Trying {Count} common CalDAV paths", pathsToTry.Length);

        foreach (var path in pathsToTry)
        {
            // First try to get principal or calendar-home-set
            var principalUrl = await FindCurrentUserPrincipalAsync(client, path, ct);
            if (principalUrl is not null)
            {
                principalUrl = MakeAbsolute(path, principalUrl);
                var homeUrl = await FindCalendarHomeSetAsync(client, principalUrl, ct);
                if (homeUrl is not null)
                {
                    homeUrl = MakeAbsolute(path, homeUrl);
                    _logger.LogInformation("Found calendars via common path {Path} → principal {Principal} → home {Home}", path, principalUrl, homeUrl);
                    return await ListCalendarsAsync(client, homeUrl, ct);
                }
            }

            // Try the path directly as a calendar home
            var calendars = await TryDirectListingAsync(client, path, ct);
            if (calendars is not null && calendars.Count > 0)
            {
                _logger.LogInformation("Found {Count} calendar(s) directly at {Path}", calendars.Count, path);
                return calendars;
            }
        }

        return null;
    }

    /// <summary>
    /// Direct PROPFIND with Depth:1 — some servers expose calendars directly at a URL.
    /// </summary>
    private async Task<List<CalDavCalendarInfo>?> TryDirectListingAsync(HttpClient client, string url, CancellationToken ct)
    {
        var calendars = await ListCalendarsAsync(client, url, ct);
        return calendars.Count > 0 ? calendars : null;
    }

    private async Task<string?> FindCurrentUserPrincipalAsync(HttpClient client, string url, CancellationToken ct)
    {
        var body = new XElement(Dav + "propfind",
            new XElement(Dav + "prop",
                new XElement(Dav + "current-user-principal"),
                new XElement(CalDav + "calendar-home-set")
            )
        ).ToString();

        var response = await SendPropfindAsync(client, url, body, "0", ct);
        if (response is null) return null;

        var href = response.Descendants(Dav + "current-user-principal")
            .Descendants(Dav + "href")
            .FirstOrDefault()?.Value;

        return href;
    }

    private async Task<string?> FindCalendarHomeSetAsync(HttpClient client, string principalUrl, CancellationToken ct)
    {
        var body = new XElement(Dav + "propfind",
            new XElement(Dav + "prop",
                new XElement(CalDav + "calendar-home-set")
            )
        ).ToString();

        var response = await SendPropfindAsync(client, principalUrl, body, "0", ct);
        if (response is null) return null;

        var href = response.Descendants(CalDav + "calendar-home-set")
            .Descendants(Dav + "href")
            .FirstOrDefault()?.Value;

        return href;
    }

    private async Task<List<CalDavCalendarInfo>> ListCalendarsAsync(HttpClient client, string calendarHomeUrl, CancellationToken ct)
    {
        var body = new XElement(Dav + "propfind",
            new XElement(Dav + "prop",
                new XElement(Dav + "resourcetype"),
                new XElement(Dav + "displayname"),
                new XElement(AppleCal + "calendar-color"),
                new XElement(CalServer + "getctag")
            )
        ).ToString();

        var response = await SendPropfindAsync(client, calendarHomeUrl, body, "1", ct);
        if (response is null) return [];

        var calendars = new List<CalDavCalendarInfo>();

        foreach (var propResponse in response.Descendants(Dav + "response"))
        {
            var resourceTypes = propResponse.Descendants(Dav + "resourcetype").FirstOrDefault();
            if (resourceTypes is null) continue;

            var isCalendar = resourceTypes.Element(CalDav + "calendar") is not null;
            if (!isCalendar) continue;

            var href = propResponse.Element(Dav + "href")?.Value ?? "";
            var displayName = propResponse.Descendants(Dav + "displayname").FirstOrDefault()?.Value ?? "Unnamed Calendar";
            var color = propResponse.Descendants(AppleCal + "calendar-color").FirstOrDefault()?.Value;

            // Normalize color (some servers return #RRGGBBAA, we want #RRGGBB)
            if (color is not null && color.Length == 9 && color.StartsWith('#'))
                color = color[..7];

            calendars.Add(new CalDavCalendarInfo(displayName, href, color, 0));
        }

        // Count events per calendar
        for (var i = 0; i < calendars.Count; i++)
        {
            var eventCount = await CountEventsAsync(client, MakeAbsolute(calendarHomeUrl, calendars[i].Path), ct);
            calendars[i] = calendars[i] with { EventCount = eventCount };
        }

        return calendars;
    }

    private async Task<int> CountEventsAsync(HttpClient client, string calendarUrl, CancellationToken ct)
    {
        // Strategy 1: REPORT calendar-query for VEVENT (RFC 4791 compliant)
        try
        {
            var body = new XElement(CalDav + "calendar-query",
                new XElement(Dav + "prop",
                    new XElement(Dav + "getetag")
                ),
                new XElement(CalDav + "filter",
                    new XElement(CalDav + "comp-filter",
                        new XAttribute("name", "VCALENDAR"),
                        new XElement(CalDav + "comp-filter",
                            new XAttribute("name", "VEVENT")
                        )
                    )
                )
            ).ToString();

            var response = await SendReportAsync(client, calendarUrl, body, ct);
            if (response is not null)
            {
                var count = response.Descendants(Dav + "response").Count();
                _logger.LogInformation("REPORT calendar-query returned {Count} event(s) for {Url}", count, calendarUrl);
                return count;
            }

            _logger.LogInformation("REPORT not supported for {Url}, falling back to PROPFIND", calendarUrl);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "REPORT failed for {Url}, falling back to PROPFIND", calendarUrl);
        }

        // Strategy 2: PROPFIND Depth:1 — count non-collection resources (individual .ics files)
        try
        {
            var body = new XElement(Dav + "propfind",
                new XElement(Dav + "prop",
                    new XElement(Dav + "resourcetype"),
                    new XElement(Dav + "getcontenttype")
                )
            ).ToString();

            var response = await SendPropfindAsync(client, calendarUrl, body, "1", ct);
            if (response is null) return 0;

            var count = 0;
            foreach (var propResponse in response.Descendants(Dav + "response"))
            {
                // Skip the calendar collection itself (it has resourcetype = collection + calendar)
                var resourceType = propResponse.Descendants(Dav + "resourcetype").FirstOrDefault();
                if (resourceType?.Element(Dav + "collection") is not null)
                    continue;

                // Count resources that are iCalendar content or have .ics in the href
                var contentType = propResponse.Descendants(Dav + "getcontenttype").FirstOrDefault()?.Value ?? "";
                var href = propResponse.Element(Dav + "href")?.Value ?? "";

                if (contentType.Contains("calendar", StringComparison.OrdinalIgnoreCase)
                    || href.EndsWith(".ics", StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            _logger.LogInformation("PROPFIND fallback counted {Count} event(s) for {Url}", count, calendarUrl);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PROPFIND fallback also failed for {Url}", calendarUrl);
            return 0;
        }
    }

    private async Task<XDocument?> SendPropfindAsync(HttpClient client, string url, string xmlBody, string depth, CancellationToken ct)
    {
        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url)
        {
            Content = new StringContent(xmlBody, Encoding.UTF8, "application/xml")
        };
        request.Headers.Add("Depth", depth);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PROPFIND request failed for {Url}", url);
            return null;
        }

        if (response.StatusCode != (HttpStatusCode)207)
        {
            _logger.LogInformation("PROPFIND {Url} returned {Status} (expected 207)", url, (int)response.StatusCode);

            // 401/403 should throw so the caller can handle auth errors
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                response.EnsureSuccessStatusCode();

            return null;
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        _logger.LogDebug("PROPFIND {Url} response:\n{Content}", url, content);
        return XDocument.Parse(content);
    }

    private async Task<XDocument?> SendReportAsync(HttpClient client, string url, string xmlBody, CancellationToken ct)
    {
        var request = new HttpRequestMessage(new HttpMethod("REPORT"), url)
        {
            Content = new StringContent(xmlBody, Encoding.UTF8, "application/xml")
        };
        request.Headers.Add("Depth", "1");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "REPORT request failed for {Url}", url);
            return null;
        }

        if (response.StatusCode != (HttpStatusCode)207)
        {
            _logger.LogDebug("REPORT {Url} returned {Status}", url, (int)response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        return XDocument.Parse(content);
    }

    private static string MakeAbsolute(string baseUrl, string href)
    {
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return href;

        var baseUri = new Uri(baseUrl);
        return new Uri(baseUri, href).ToString();
    }
}
