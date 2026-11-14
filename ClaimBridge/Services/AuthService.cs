using ClaimBridge.Data;
using ClaimBridge.DTOs;
using ClaimBridge.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Services;

public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AppDbContext dbContext,
    ITokenService tokenService) : IAuthService
{
    private const string InvalidCredentialsMessage = "Invalid email or password";
    private const string InvalidRefreshTokenMessage = "Invalid refresh token";

    public async Task<ServiceResult<UserProfileResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var existingUser = await userManager.FindByEmailAsync(request.Email);
        if (existingUser is not null)
        {
            return ServiceResult<UserProfileResponse>.Invalid(new Dictionary<string, string[]>
            {
                ["email"] = ["Email is already registered."]
            });
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            LockoutEnabled = true
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return ServiceResult<UserProfileResponse>.Invalid(ToErrors(createResult, "password"));
        }

        var roleResult = await userManager.AddToRoleAsync(user, RoleSeeder.UserRole);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return ServiceResult<UserProfileResponse>.Invalid(ToErrors(roleResult, "role"));
        }

        return ServiceResult<UserProfileResponse>.Success(
            ToProfile(user, new[] { RoleSeeder.UserRole }));
    }

    public async Task<ServiceResult<AuthResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return ServiceResult<AuthResponse>.Unauthorized(InvalidCredentialsMessage);
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (!signInResult.Succeeded)
        {
            return ServiceResult<AuthResponse>.Unauthorized(InvalidCredentialsMessage);
        }

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<ServiceResult<AuthResponse>> RefreshAsync(
        RefreshRequest request,
        CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        var storedToken = await dbContext.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (storedToken is null)
        {
            return ServiceResult<AuthResponse>.Unauthorized(InvalidRefreshTokenMessage);
        }

        if (storedToken.RevokedAt is not null)
        {
            await RevokeAllActiveTokensAsync(storedToken.UserId, cancellationToken);
            return ServiceResult<AuthResponse>.Unauthorized(InvalidRefreshTokenMessage);
        }

        var now = DateTime.UtcNow;
        if (storedToken.ExpiresAt <= now)
        {
            return ServiceResult<AuthResponse>.Unauthorized(InvalidRefreshTokenMessage);
        }

        var user = storedToken.User;
        var roles = await userManager.GetRolesAsync(user);
        var accessToken = tokenService.CreateAccessToken(user, roles);
        var newRefreshToken = tokenService.GenerateRefreshToken();
        var newTokenHash = tokenService.HashRefreshToken(newRefreshToken);

        storedToken.RevokedAt = now;
        storedToken.ReplacedByTokenHash = newTokenHash;

        dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newTokenHash,
            CreatedAt = now,
            ExpiresAt = now.Add(tokenService.RefreshTokenLifetime)
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<AuthResponse>.Success(new AuthResponse(
            accessToken.Token,
            newRefreshToken,
            accessToken.ExpiresAtUtc,
            ToProfile(user, roles)));
    }

    public async Task LogoutAsync(string userId, string? refreshToken, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var tokenHash = tokenService.HashRefreshToken(refreshToken);
            var storedToken = await dbContext.RefreshTokens
                .SingleOrDefaultAsync(
                    token => token.UserId == userId && token.TokenHash == tokenHash,
                    cancellationToken);

            if (storedToken is not null && storedToken.RevokedAt is null)
            {
                storedToken.RevokedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        await RevokeAllActiveTokensAsync(userId, cancellationToken);
    }

    public async Task<ServiceResult<UserProfileResponse>> GetProfileAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return ServiceResult<UserProfileResponse>.NotFound("User not found.");
        }

        var roles = await userManager.GetRolesAsync(user);
        return ServiceResult<UserProfileResponse>.Success(ToProfile(user, roles));
    }

    public async Task<ServiceResult<bool>> ChangePasswordAsync(
        string userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return ServiceResult<bool>.NotFound("User not found.");
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return ServiceResult<bool>.Invalid(ToErrors(result, "newPassword"));
        }

        await RevokeAllActiveTokensAsync(userId, cancellationToken);
        return ServiceResult<bool>.Success(true);
    }

    private async Task<ServiceResult<AuthResponse>> IssueTokensAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var accessToken = tokenService.CreateAccessToken(user, roles);
        var refreshToken = tokenService.GenerateRefreshToken();
        var now = DateTime.UtcNow;

        dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.HashRefreshToken(refreshToken),
            CreatedAt = now,
            ExpiresAt = now.Add(tokenService.RefreshTokenLifetime)
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<AuthResponse>.Success(new AuthResponse(
            accessToken.Token,
            refreshToken,
            accessToken.ExpiresAtUtc,
            ToProfile(user, roles)));
    }

    private async Task RevokeAllActiveTokensAsync(string userId, CancellationToken cancellationToken)
    {
        var activeTokens = await dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        if (activeTokens.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var token in activeTokens)
        {
            token.RevokedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Dictionary<string, string[]> ToErrors(IdentityResult result, string field) =>
        new() { [field] = result.Errors.Select(error => error.Description).ToArray() };

    private static UserProfileResponse ToProfile(ApplicationUser user, IEnumerable<string> roles) =>
        new(user.Id, user.Email ?? string.Empty, user.FirstName, user.LastName, roles.ToArray());
}
