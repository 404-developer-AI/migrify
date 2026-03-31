using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Migrify.Core.Interfaces;
using Migrify.Core.Models;

namespace Migrify.Infrastructure.Services;

public class GoogleOAuthTokenService : IOAuthTokenService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleOAuthTokenService> _logger;

    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string Scope = "https://mail.google.com/";

    public GoogleOAuthTokenService(IHttpClientFactory httpClientFactory, ILogger<GoogleOAuthTokenService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string GetAuthorizationUrl(Guid projectId, string clientId, string redirectUri)
    {
        var state = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{projectId}"));

        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state
        };

        var queryString = string.Join("&", query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return $"{AuthEndpoint}?{queryString}";
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(
        string code, string clientId, string clientSecret, string redirectUri)
    {
        _logger.LogDebug("Exchanging authorization code for tokens");

        var client = _httpClientFactory.CreateClient("GoogleOAuth");
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        });

        var response = await client.PostAsync(TokenEndpoint, content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Google token exchange failed: {StatusCode} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Google token exchange failed: {body}");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>()
            ?? throw new InvalidOperationException("Failed to parse Google token response");

        if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
        {
            _logger.LogWarning("No refresh token received — user may have previously authorized this app");
            throw new InvalidOperationException(
                "No refresh token received. Revoke access at https://myaccount.google.com/permissions and try again.");
        }

        _logger.LogDebug("Token exchange successful, expires in {ExpiresIn}s", tokenResponse.ExpiresIn);

        return new OAuthTokenResult(
            tokenResponse.AccessToken,
            tokenResponse.RefreshToken,
            DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn));
    }

    public async Task<OAuthTokenResult> RefreshTokenAsync(
        string refreshToken, string clientId, string clientSecret)
    {
        _logger.LogDebug("Refreshing OAuth2 access token");

        var client = _httpClientFactory.CreateClient("GoogleOAuth");
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "refresh_token"
        });

        var response = await client.PostAsync(TokenEndpoint, content);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("Google token refresh failed: {StatusCode} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Token refresh failed. Re-authorization required.");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>()
            ?? throw new InvalidOperationException("Failed to parse Google token response");

        _logger.LogDebug("Token refresh successful, expires in {ExpiresIn}s", tokenResponse.ExpiresIn);

        return new OAuthTokenResult(
            tokenResponse.AccessToken,
            refreshToken, // Google doesn't return a new refresh token on refresh
            DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn));
    }

    private class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }
}
