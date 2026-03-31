namespace Migrify.Core.Models;

public record OAuthTokenResult(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc);
