using System.ComponentModel.DataAnnotations;

namespace ClaimBridge.DTOs;

public sealed class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
