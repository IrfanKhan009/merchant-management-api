using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MerchantManagement.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MerchantManagement.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    /// <summary>HMAC-SHA256 signing key. Supplied via configuration only; minimum 32 characters.</summary>
    public string Key { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 30;
}

public static class AuthPolicies
{
    public const string Read = "Read";          // Viewer, Manager, Admin
    public const string Write = "Write";        // Manager, Admin
    public const string AdminOnly = "AdminOnly"; // Admin
}

public sealed class TokenService(IOptions<JwtOptions> options)
{
    public (string Token, DateTime ExpiresAtUtc) Create(User user, DateTime? nowUtc = null)
    {
        var o = options.Value;
        var now = nowUtc ?? DateTime.UtcNow;
        var expires = now.AddMinutes(o.ExpiryMinutes);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Claims deliberately exclude anything secret (no password hash).
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim("role", user.Role)
        };

        var token = new JwtSecurityToken(o.Issuer, o.Audience, claims, notBefore: now, expires: expires,
            signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
