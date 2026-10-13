using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MerchantManagement.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MerchantManagement.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@example.test";
    public const string AdminPassword = "Test-Admin-Password-123!";
    public const string UserPassword = "Another-Long-Password-1!";
    public const string JwtKey = "integration-test-signing-key-integration-1234";
    public const string Issuer = "merchant-api-tests";
    public const string Audience = "merchant-api-test-clients";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mm-api-{Guid.NewGuid():N}");
    private readonly ConcurrentDictionary<string, Task<string>> _tokens = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dir);
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(_dir, "test.db")}");
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Bootstrap:AdminEmail", AdminEmail);
        builder.UseSetting("Bootstrap:AdminPassword", AdminPassword);
    }

    /// <summary>Returns a client authenticated as the given role, or anonymous when role is null.</summary>
    public async Task<HttpClient> ClientAsync(string? role)
    {
        var client = CreateClient();
        if (role is null) return client;
        var token = await _tokens.GetOrAdd(role, CreateTokenAsync);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<string?> LoginAsync(string email, string password)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });
        if (!response.IsSuccessStatusCode) return null;
        return (await response.Content.ReadFromJsonAsync<LoginView>())!.AccessToken;
    }

    public async Task<(string Email, Guid Id)> CreateUserAsync(string role)
    {
        var admin = await ClientAsync(Roles.Admin);
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        var response = await admin.PostAsJsonAsync("/api/users", new { email, password = UserPassword, role });
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<UserView>())!;
        return (email, created.Id);
    }

    private async Task<string> CreateTokenAsync(string role)
    {
        if (role == Roles.Admin) return (await LoginAsync(AdminEmail, AdminPassword))!;
        var (email, _) = await CreateUserAsync(role);
        return (await LoginAsync(email, UserPassword))!;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}

// ---- Response shapes used by tests (JSON enums are strings) ----
public sealed record LoginView(string AccessToken, string TokenType, DateTime ExpiresAtUtc, string Role);
public sealed record UserView(Guid Id, string Email, string Role, bool IsActive);
public sealed record MerchantView(Guid Id, string Name, string LegalName, string Email, string CountryCode,
    string Status, int SubMerchantCount);
public sealed record SubMerchantView(Guid Id, Guid MerchantId, string Name, string ExternalReference, string Status);
public sealed record Paged<T>(List<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public static class TestHelpers
{
    public static string Unique() => Guid.NewGuid().ToString("N")[..10];

    public static async Task<MerchantView> CreateMerchantAsync(
        HttpClient client, string? name = null, string? email = null, string country = "PK")
    {
        var u = Unique();
        var response = await client.PostAsJsonAsync("/api/merchants", new
        {
            name = name ?? $"Merchant {u}", legalName = "Legal Entity Ltd",
            email = email ?? $"m-{u}@example.test", countryCode = country
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MerchantView>())!;
    }

    public static Task<HttpResponseMessage> UpdateMerchantAsync(
        HttpClient client, MerchantView m, string status, string? name = null) =>
        client.PutAsJsonAsync($"/api/merchants/{m.Id}", new
        {
            name = name ?? m.Name, legalName = m.LegalName, email = m.Email, countryCode = m.CountryCode, status
        });
}
