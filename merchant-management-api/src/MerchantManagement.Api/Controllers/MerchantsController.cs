using MerchantManagement.Api.Auth;
using MerchantManagement.Api.Contracts;
using MerchantManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerchantManagement.Api.Controllers;

[ApiController]
[Route("api/merchants")]
public sealed class MerchantsController(MerchantService merchants) : ControllerBase
{
    /// <summary>List merchants with search, status filter, sorting and paging.</summary>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.Read)]
    public async Task<ActionResult<PagedResult<MerchantResponse>>> List([FromQuery] MerchantQuery query, CancellationToken ct) =>
        Ok(await merchants.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Read)]
    public async Task<ActionResult<MerchantResponse>> Get(Guid id, CancellationToken ct) =>
        Ok(await merchants.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = AuthPolicies.Write)]
    [ProducesResponseType<MerchantResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<MerchantResponse>> Create(CreateMerchantRequest request, CancellationToken ct)
    {
        var created = await merchants.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Write)]
    public async Task<ActionResult<MerchantResponse>> Update(Guid id, UpdateMerchantRequest request, CancellationToken ct) =>
        Ok(await merchants.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await merchants.DeleteAsync(id, ct);
        return NoContent();
    }
}
