using ClaimBridge.DTOs;

namespace ClaimBridge.Services;

public interface IAuthService
{
    Task<ServiceResult<UserProfileResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<ServiceResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<ServiceResult<AuthResponse>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken);

    Task LogoutAsync(string userId, string? refreshToken, CancellationToken cancellationToken);

    Task<ServiceResult<UserProfileResponse>> GetProfileAsync(string userId, CancellationToken cancellationToken);

    Task<ServiceResult<bool>> ChangePasswordAsync(string userId, ChangePasswordRequest request, CancellationToken cancellationToken);
}
