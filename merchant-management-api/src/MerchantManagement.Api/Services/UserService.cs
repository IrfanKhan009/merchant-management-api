using MerchantManagement.Api.Contracts;
using MerchantManagement.Api.Data;
using MerchantManagement.Api.Domain;
using MerchantManagement.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MerchantManagement.Api.Services;

public sealed class UserService(AppDbContext db, IPasswordHasher<User> hasher, ILogger<UserService> logger)
{
    public async Task<UserResponse> CreateAsync(CreateUserRequest r, CancellationToken ct)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("A user with this email already exists.");

        var user = new User
        {
            Id = Guid.NewGuid(), Email = email, Role = r.Role, IsActive = true, CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, r.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} created with role {Role}", user.Id, user.Role);
        return ToResponse(user);
    }

    public async Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking().OrderBy(u => u.Email).Take(200)
            .Select(u => new UserResponse(u.Id, u.Email, u.Role, u.IsActive, u.CreatedAt))
            .ToListAsync(ct);

    public async Task DeactivateAsync(Guid id, Guid currentUserId, CancellationToken ct)
    {
        if (id == currentUserId) throw new ConflictException("You cannot deactivate your own account.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
                   ?? throw new NotFoundException("User not found.");
        user.IsActive = false;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("User {UserId} deactivated by {ActorId}", id, currentUserId);
    }

    private static UserResponse ToResponse(User u) => new(u.Id, u.Email, u.Role, u.IsActive, u.CreatedAt);
}
