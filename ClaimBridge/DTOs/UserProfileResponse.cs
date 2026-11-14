namespace ClaimBridge.DTOs;

public sealed record UserProfileResponse(
    string Id,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyList<string> Roles);
