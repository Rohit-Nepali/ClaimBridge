using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace ClaimBridge.Models;

public class ApplicationUser : IdentityUser
{
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    public string FullName => $"{FirstName} {LastName}".Trim();

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
