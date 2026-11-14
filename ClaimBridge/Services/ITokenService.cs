using ClaimBridge.Models;

namespace ClaimBridge.Services;

public interface ITokenService
{
    AccessTokenResult CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);

    string GenerateRefreshToken();

    string HashRefreshToken(string refreshToken);

    TimeSpan AccessTokenLifetime { get; }

    TimeSpan RefreshTokenLifetime { get; }
}

public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);
