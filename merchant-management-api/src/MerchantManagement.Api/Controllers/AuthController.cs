using MerchantManagement.Api.Contracts;
using MerchantManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerchantManagement.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    /// <summary>Exchange email + password for a short-lived JWT access token.</summary>
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);
        // One generic message for unknown user, wrong password and disabled account.
        return result is null
            ? Problem(title: "Invalid credentials", detail: "Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized)
            : Ok(result);
    }
}
