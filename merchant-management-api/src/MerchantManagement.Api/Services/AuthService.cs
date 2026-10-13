using MerchantManagement.Api.Auth;
using MerchantManagement.Api.Contracts;
using MerchantManagement.Api.Data;
using MerchantManagement.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MerchantManagement.Api.Services;

public sealed class AuthService(
    AppDbContext db, IPasswordHasher<User> hasher, TokenService tokens, ILogger<AuthService> logger)
{
    // Used to spend comparable time when the account does not exist (reduces user-enumeration via timing).
    private static readonly User DummyUser = new();
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(DummyUser, Guid.NewGuid().ToString("N"));

    /// <returns>A token, or null when credentials are invalid or the account is disabled (callers return one generic 401).</returns>
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);

        var result = hasher.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash, request.Password);
        if (user is null || !user.IsActive || result == PasswordVerificationResult.Failed)
        {
            // Never log the submitted email or password.
            logger.LogWarning("Failed login attempt");
            return null;
        }

        var (token, expires) = tokens.Create(user);
        logger.LogInformation("User {UserId} logged in with role {Role}", user.Id, user.Role);
        return new LoginResponse(token, "Bearer", expires, user.Role);
    }
}
