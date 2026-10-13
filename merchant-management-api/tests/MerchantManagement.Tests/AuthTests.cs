using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using MerchantManagement.Api.Data;
using MerchantManagement.Api.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace MerchantManagement.Tests;

public sealed class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Login_with_valid_credentials_returns_a_bearer_token()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<LoginView>())!;
        Assert.Equal("Bearer", body.TokenType);
        Assert.Equal("Admin", body.Role);
        Assert.Equal(3, body.AccessToken.Split('.').Length);
        Assert.True(body.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_return_the_same_401()
    {
        var client = factory.CreateClient();
        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = "definitely-wrong-password" });
        var unknownUser = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@example.test", password = "definitely-wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal(Strip(await wrongPassword.Content.ReadAsStringAsync()),
                     Strip(await unknownUser.Content.ReadAsStringAsync()));
    }

    // Removes per-request ids so two responses can be compared.
    private static string Strip(string json) =>
        System.Text.RegularExpressions.Regex.Replace(json, "\"(traceId|correlationId)\":\"[^\"]*\"", "");

    [Fact]
    public async Task Protected_endpoints_require_a_token()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/merchants")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Garbage_wrong_key_and_expired_tokens_are_rejected()
    {
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/merchants")).StatusCode);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Forge("a-completely-different-signing-key-0000000", DateTime.UtcNow.AddHours(1)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/merchants")).StatusCode);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Forge(ApiFactory.JwtKey, DateTime.UtcNow.AddHours(-1)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/merchants")).StatusCode);

        // Sanity check: the same forging helper with the right key and a future expiry is accepted.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Forge(ApiFactory.JwtKey, DateTime.UtcNow.AddHours(1)));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/merchants")).StatusCode);
    }

    private static string Forge(string key, DateTime expiresUtc)
    {
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(ApiFactory.Issuer, ApiFactory.Audience,
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", Roles.Viewer)],
            notBefore: expiresUtc.AddHours(-2), expires: expiresUtc, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task Passwords_are_stored_hashed()
    {
        var (email, _) = await factory.CreateUserAsync(Roles.Viewer);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Email == email);

        Assert.NotEqual(ApiFactory.UserPassword, user.PasswordHash);
        Assert.DoesNotContain(ApiFactory.UserPassword, user.PasswordHash);
        Assert.True(user.PasswordHash.Length > 40);
    }

    [Fact]
    public async Task Only_admins_can_manage_users()
    {
        var manager = await factory.ClientAsync(Roles.Manager);
        var viewer = await factory.ClientAsync(Roles.Viewer);
        var body = new { email = $"x-{TestHelpers.Unique()}@example.test", password = ApiFactory.UserPassword, role = "Viewer" };

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/users", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Create_user_validates_input_and_rejects_duplicates()
    {
        var admin = await factory.ClientAsync(Roles.Admin);
        var email = $"dup-{TestHelpers.Unique()}@example.test";

        var weak = await admin.PostAsJsonAsync("/api/users", new { email, password = "short", role = "Viewer" });
        var badRole = await admin.PostAsJsonAsync("/api/users", new { email, password = ApiFactory.UserPassword, role = "Root" });
        var badEmail = await admin.PostAsJsonAsync("/api/users", new { email = "nope", password = ApiFactory.UserPassword, role = "Viewer" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badEmail.StatusCode);

        var ok = await admin.PostAsJsonAsync("/api/users", new { email, password = ApiFactory.UserPassword, role = "Viewer" });
        var dup = await admin.PostAsJsonAsync("/api/users", new { email = email.ToUpperInvariant(), password = ApiFactory.UserPassword, role = "Viewer" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_can_no_longer_log_in()
    {
        var (email, id) = await factory.CreateUserAsync(Roles.Viewer);
        Assert.NotNull(await factory.LoginAsync(email, ApiFactory.UserPassword));

        var admin = await factory.ClientAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{id}/deactivate", null)).StatusCode);

        Assert.Null(await factory.LoginAsync(email, ApiFactory.UserPassword));
    }

    [Fact]
    public async Task Admin_cannot_deactivate_themselves()
    {
        var admin = await factory.ClientAsync(Roles.Admin);
        var users = (await admin.GetFromJsonAsync<List<UserView>>("/api/users"))!;
        var self = users.Single(u => u.Email == ApiFactory.AdminEmail);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/users/{self.Id}/deactivate", null)).StatusCode);
    }
}
