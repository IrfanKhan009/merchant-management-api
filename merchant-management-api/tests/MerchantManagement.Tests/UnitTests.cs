using System.IdentityModel.Tokens.Jwt;
using MerchantManagement.Api.Auth;
using MerchantManagement.Api.Contracts;
using MerchantManagement.Api.Domain;
using MerchantManagement.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MerchantManagement.Tests;

public class GlobalExceptionHandlerTests
{
    private static async Task<(int Status, string Body)> RunAsync(Exception ex)
    {
        var ctx = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();

        var handled = await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
            .TryHandleAsync(ctx, ex, CancellationToken.None);

        Assert.True(handled);
        ctx.Response.Body.Position = 0;
        return (ctx.Response.StatusCode, await new StreamReader(ctx.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task NotFound_maps_to_404() => Assert.Equal(404, (await RunAsync(new NotFoundException("nope"))).Status);

    [Fact]
    public async Task Conflict_maps_to_409() => Assert.Equal(409, (await RunAsync(new ConflictException("dup"))).Status);

    [Fact]
    public async Task Unique_constraint_violation_maps_to_409()
    {
        var ex = new DbUpdateException("save failed", new SqliteException("UNIQUE constraint failed: Merchants.Email", 19));
        var (status, body) = await RunAsync(ex);

        Assert.Equal(409, status);
        Assert.DoesNotContain("Merchants.Email", body); // no schema details leak
    }

    [Fact]
    public async Task Unexpected_errors_return_500_without_leaking_details()
    {
        var (status, body) = await RunAsync(new InvalidOperationException("Server=prod-db;Password=hunter2"));

        Assert.Equal(500, status);
        Assert.DoesNotContain("hunter2", body);
        Assert.DoesNotContain("prod-db", body);
    }
}

public class TokenServiceTests
{
    private static TokenService Service(int minutes = 30) => new(Options.Create(new JwtOptions
    {
        Issuer = "iss", Audience = "aud", Key = "unit-test-signing-key-unit-test-1234", ExpiryMinutes = minutes
    }));

    [Fact]
    public void Token_carries_identity_and_role_but_no_secrets()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "m@example.test", Role = Roles.Manager, PasswordHash = "SUPER-SECRET-HASH" };
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var (token, expires) = Service(30).Create(user, now);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal("Manager", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.Equal(now.AddMinutes(30), expires);
        Assert.Equal("iss", jwt.Issuer);
        Assert.DoesNotContain(jwt.Claims, c => c.Value.Contains("SECRET"));
        Assert.DoesNotContain("SECRET", token);
    }

    [Fact]
    public void Each_token_has_a_unique_id()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "m@example.test", Role = Roles.Viewer };
        Assert.NotEqual(Service().Create(user).Token, Service().Create(user).Token);
    }
}

public class PagedResultTests
{
    [Theory]
    [InlineData(25, 10, 3)]
    [InlineData(20, 10, 2)]
    [InlineData(0, 10, 0)]
    [InlineData(1, 100, 1)]
    public void TotalPages_rounds_up(int total, int size, int expected) =>
        Assert.Equal(expected, new PagedResult<int>([], 1, size, total).TotalPages);
}
