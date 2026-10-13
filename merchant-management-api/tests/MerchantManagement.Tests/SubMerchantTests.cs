using System.Net;
using System.Net.Http.Json;
using MerchantManagement.Api.Domain;
using static MerchantManagement.Tests.TestHelpers;

namespace MerchantManagement.Tests;

public sealed class SubMerchantTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> PostSub(HttpClient c, Guid merchantId, string reference, string name = "Branch") =>
        c.PostAsJsonAsync($"/api/merchants/{merchantId}/sub-merchants", new { name, externalReference = reference });

    [Fact]
    public async Task Create_get_and_count_on_parent()
    {
        var manager = await factory.ClientAsync(Roles.Manager);
        var m = await CreateMerchantAsync(manager);

        var response = await PostSub(manager, m.Id, "STORE-001", "Lahore Store");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sub = (await response.Content.ReadFromJsonAsync<SubMerchantView>())!;
        Assert.Equal("Pending", sub.Status);
        Assert.Equal(m.Id, sub.MerchantId);

        var fetched = await manager.GetFromJsonAsync<SubMerchantView>($"/api/merchants/{m.Id}/sub-merchants/{sub.Id}");
        Assert.Equal("STORE-001", fetched!.ExternalReference);

        var parent = await manager.GetFromJsonAsync<MerchantView>($"/api/merchants/{m.Id}");
        Assert.Equal(1, parent!.SubMerchantCount);
    }

    [Fact]
    public async Task External_reference_is_unique_per_merchant_but_reusable_across_merchants()
    {
        var manager = await factory.ClientAsync(Roles.Manager);
        var a = await CreateMerchantAsync(manager);
        var b = await CreateMerchantAsync(manager);

        Assert.Equal(HttpStatusCode.Created, (await PostSub(manager, a.Id, "REF-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostSub(manager, a.Id, "REF-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostSub(manager, b.Id, "REF-1")).StatusCode);
    }

    [Fact]
    public async Task Validation_and_missing_parent()
    {
        var manager = await factory.ClientAsync(Roles.Manager);
        var m = await CreateMerchantAsync(manager);

        Assert.Equal(HttpStatusCode.BadRequest, (await PostSub(manager, m.Id, "has spaces!")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostSub(manager, m.Id, "OK-1", name: "x")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostSub(manager, Guid.NewGuid(), "OK-2")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await manager.GetAsync($"/api/merchants/{Guid.NewGuid()}/sub-merchants")).StatusCode);
    }

    [Fact]
    public async Task Cannot_add_sub_merchants_to_suspended_or_closed_merchants()
    {
        var manager = await factory.ClientAsync(Roles.Manager);
        var suspended = await CreateMerchantAsync(manager);
        var closed = await CreateMerchantAsync(manager);
        await UpdateMerchantAsync(manager, suspended, "Suspended");
        await UpdateMerchantAsync(manager, closed, "Closed");

        Assert.Equal(HttpStatusCode.Conflict, (await PostSub(manager, suspended.Id, "S-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostSub(manager, closed.Id, "C-1")).StatusCode);
    }

    [Fact]
    public async Task Sub_merchant_is_only_reachable_through_its_own_parent()
    {
        var manager = await factory.ClientAsync(Roles.Manager);
        var a = await CreateMerchantAsync(manager);
        var b = await CreateMerchantAsync(manager);
        var sub = (await (await PostSub(manager, a.Id, "X-1")).Content.ReadFromJsonAsync<SubMerchantView>())!;

        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/merchants/{b.Id}/sub-merchants/{sub.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.DeleteAsync($"/api/merchants/{b.Id}/sub-merchants/{sub.Id}")).StatusCode);
    }

    [Fact]
    public async Task Update_delete_and_rbac()
    {
        var viewer = await factory.ClientAsync(Roles.Viewer);
        var manager = await factory.ClientAsync(Roles.Manager);
        var m = await CreateMerchantAsync(manager);
        var sub = (await (await PostSub(manager, m.Id, "U-1")).Content.ReadFromJsonAsync<SubMerchantView>())!;
        var url = $"/api/merchants/{m.Id}/sub-merchants/{sub.Id}";

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync(url, new { name = "Renamed", status = "Active" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync(url)).StatusCode);

        var updated = await manager.PutAsJsonAsync(url, new { name = "Renamed", status = "Active" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Active", (await updated.Content.ReadFromJsonAsync<SubMerchantView>())!.Status);

        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task List_supports_paging_search_and_sorting()
    {
        var manager = await factory.ClientAsync(Roles.Manager);
        var m = await CreateMerchantAsync(manager);
        for (var i = 1; i <= 12; i++) await PostSub(manager, m.Id, $"R-{i:D2}", $"Branch {i:D2}");

        var page = (await manager.GetFromJsonAsync<Paged<SubMerchantView>>(
            $"/api/merchants/{m.Id}/sub-merchants?pageSize=5&page=3&sortBy=name"))!;
        Assert.Equal(12, page.TotalCount);
        Assert.Equal(2, page.Items.Count);

        var search = (await manager.GetFromJsonAsync<Paged<SubMerchantView>>(
            $"/api/merchants/{m.Id}/sub-merchants?search=R-07"))!;
        Assert.Single(search.Items);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.GetAsync($"/api/merchants/{m.Id}/sub-merchants?sortBy=externalReference")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_merchant_removes_its_sub_merchants()
    {
        var admin = await factory.ClientAsync(Roles.Admin);
        var m = await CreateMerchantAsync(admin);
        var sub = (await (await PostSub(admin, m.Id, "D-1")).Content.ReadFromJsonAsync<SubMerchantView>())!;

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/merchants/{m.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/merchants/{m.Id}/sub-merchants/{sub.Id}")).StatusCode);
    }
}
