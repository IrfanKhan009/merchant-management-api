using MerchantManagement.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MerchantManagement.Api.Data;

/// <summary>
/// Creates the schema and, only when no users exist, bootstraps the first Admin from configuration.
/// There is no default password: if Bootstrap settings are absent nobody can log in until they are provided.
/// </summary>
public sealed class DbInitializer(IServiceProvider services, IConfiguration config, ILogger<DbInitializer> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync(ct);

        if (await db.Users.AnyAsync(ct)) return;

        var email = config["Bootstrap:AdminEmail"];
        var password = config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("No users exist and Bootstrap:AdminEmail / Bootstrap:AdminPassword are not set; login is impossible until they are.");
            return;
        }
        if (password.Length < 12)
            throw new InvalidOperationException("Bootstrap:AdminPassword must be at least 12 characters.");

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var admin = new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            Role = Roles.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        admin.PasswordHash = hasher.HashPassword(admin, password);
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Bootstrap admin account created");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
