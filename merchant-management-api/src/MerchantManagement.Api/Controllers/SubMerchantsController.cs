using MerchantManagement.Api.Auth;
using MerchantManagement.Api.Contracts;
using MerchantManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MerchantManagement.Api.Controllers;

[ApiController]
[Route("api/merchants/{merchantId:guid}/sub-merchants")]
public sealed class SubMerchantsController(SubMerchantService subMerchants) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthPolicies.Read)]
    public async Task<ActionResult<PagedResult<SubMerchantResponse>>> List(
        Guid merchantId, [FromQuery] SubMerchantQuery query, CancellationToken ct) =>
        Ok(await subMerchants.ListAsync(merchantId, query, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Read)]
    public async Task<ActionResult<SubMerchantResponse>> Get(Guid merchantId, Guid id, CancellationToken ct) =>
        Ok(await subMerchants.GetAsync(merchantId, id, ct));

    [HttpPost]
    [Authorize(Policy = AuthPolicies.Write)]
    [ProducesResponseType<SubMerchantResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SubMerchantResponse>> Create(
        Guid merchantId, CreateSubMerchantRequest request, CancellationToken ct)
    {
        var created = await subMerchants.CreateAsync(merchantId, request, ct);
        return CreatedAtAction(nameof(Get), new { merchantId, id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Write)]
    public async Task<ActionResult<SubMerchantResponse>> Update(
        Guid merchantId, Guid id, UpdateSubMerchantRequest request, CancellationToken ct) =>
        Ok(await subMerchants.UpdateAsync(merchantId, id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Write)]
    public async Task<IActionResult> Delete(Guid merchantId, Guid id, CancellationToken ct)
    {
        await subMerchants.DeleteAsync(merchantId, id, ct);
        return NoContent();
    }
}
