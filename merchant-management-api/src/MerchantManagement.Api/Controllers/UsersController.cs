using MerchantManagement.Api.Auth;
using MerchantManagement.Api.Contracts;
using MerchantManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerchantManagement.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class UsersController(UserService users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> List(CancellationToken ct) =>
        Ok(await users.ListAsync(ct));

    [HttpPost]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var created = await users.CreateAsync(request, ct);
        return Created($"/api/users/{created.Id}", created);
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var currentUserId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : Guid.Empty;
        await users.DeactivateAsync(id, currentUserId, ct);
        return NoContent();
    }
}
