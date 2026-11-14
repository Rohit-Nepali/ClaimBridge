using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ClaimBridge.Common;
using ClaimBridge.Models;
using ClaimBridge.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ClaimBridge.Services;

public sealed class TokenService(IOptions<JwtSettings> options) : ITokenService
{
    private static readonly JsonWebTokenHandler Handler = new();
    private readonly JwtSettings _settings = options.Value;

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(_settings.AccessTokenMinutes);

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_settings.RefreshTokenDays);

    public AccessTokenResult CreateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimNames.Sub, user.Id),
            new(ClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimNames.Name, user.FullName),
            new(ClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var expiresAt = DateTime.UtcNow.Add(AccessTokenLifetime);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt,
            NotBefore = DateTime.UtcNow,
            IssuedAt = DateTime.UtcNow,
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
        };

        var token = Handler.CreateToken(descriptor);
        return new AccessTokenResult(token, expiresAt);
    }

    public string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public string HashRefreshToken(string refreshToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToBase64String(hash);
    }
}
