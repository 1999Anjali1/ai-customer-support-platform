using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SmartSupport.Api.Auth;
using SmartSupport.Application.Abstractions;
using SmartSupport.Domain.Entities;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Api.Controllers;

public record LoginRequest(string Email, string Password);

public record UserDto(Guid Id, string Email, string FullName, UserRole Role, string PreferredLanguage);

public record LoginResponse(string Token, DateTime ExpiresAt, UserDto User);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAppDbContext db, IPasswordHasher<User> hasher, JwtTokenService tokens) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null || string.IsNullOrEmpty(request.Password) ||
            hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");
        }

        var (token, expiresAt) = tokens.Create(user);
        return new LoginResponse(token, expiresAt, ToDto(user));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var id = User.GetUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();
        return ToDto(user);
    }

    private static UserDto ToDto(User u) => new(u.Id, u.Email, u.FullName, u.Role, u.PreferredLanguage);
}
