using SmartSupport.Domain.Enums;

namespace SmartSupport.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string FullName { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    /// <summary>"en" or "ar" – used for the UI and the AI's first greeting.</summary>
    public string PreferredLanguage { get; set; } = "en";

    /// <summary>Only customers are linked to a telecom account.</summary>
    public Guid? CustomerAccountId { get; set; }
    public CustomerAccount? CustomerAccount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
