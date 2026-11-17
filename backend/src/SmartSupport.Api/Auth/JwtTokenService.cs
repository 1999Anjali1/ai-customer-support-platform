using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartSupport.Domain.Entities;

namespace SmartSupport.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "SmartSupport";
    public string Audience { get; set; } = "SmartSupport.Web";

    /// <summary>At least 32 characters. Set via environment variable Jwt__Key in production.</summary>
    public string Key { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 480;

    public SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(Key));
}

public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTime ExpiresAt) Create(User user)
    {
        var jwt = options.Value;
        var expires = DateTime.UtcNow.AddMinutes(jwt.ExpiryMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Expires = expires,
            SigningCredentials = new SigningCredentials(jwt.SigningKey(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [AppClaims.UserId] = user.Id.ToString(),
                [AppClaims.Email] = user.Email,
                [AppClaims.Name] = user.FullName,
                [AppClaims.Role] = user.Role.ToString(),
                [AppClaims.Language] = user.PreferredLanguage
            }
        };

        return (_handler.CreateToken(descriptor), expires);
    }
}

/// <summary>Short JWT claim names (we turn off .NET's legacy claim-type mapping).</summary>
public static class AppClaims
{
    public const string UserId = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string Language = "lang";
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(AppClaims.UserId), out var id)
            ? id
            : throw new UnauthorizedAccessException("Missing user id claim.");

    public static bool IsStaff(this ClaimsPrincipal user) =>
        user.IsInRole("Agent") || user.IsInRole("Admin");
}
