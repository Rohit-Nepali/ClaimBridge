namespace ClaimBridge.DTOs;

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAtUtc,
    UserProfileResponse User)
{
    public string TokenType { get; init; } = "Bearer";
}
