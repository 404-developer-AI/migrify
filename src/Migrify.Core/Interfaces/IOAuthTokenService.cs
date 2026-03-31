using Migrify.Core.Models;

namespace Migrify.Core.Interfaces;

public interface IOAuthTokenService
{
    string GetAuthorizationUrl(Guid jobId, string clientId, string redirectUri);

    Task<OAuthTokenResult> ExchangeCodeAsync(string code, string clientId, string clientSecret, string redirectUri);

    Task<OAuthTokenResult> RefreshTokenAsync(string refreshToken, string clientId, string clientSecret);
}
