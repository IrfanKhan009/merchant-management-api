using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MerchantManagement.Api.Domain;
using static MerchantManagement.Tests.TestHelpers;

namespace MerchantManagement.Tests;

public sealed class MerchantTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Create_returns_201_with_location_and_pending_status()
    {
        var manager = await factory.ClientAsync(Roles.Manager);
        var u = Unique();

        var response = await manager.PostAsJsonAsync("/api/merchants", new
        {
            name = $"  Acme {u}  ", legalName = "Acme Holdings Ltd", email = $"Ops-{u}@Example.test", countryCode = "PK"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var created = (await response.Content.ReadFromJsonAsync<MerchantView>())!;
        Assert.Equal("Pending", created.Status);
        Assert.Equal($"Acme {u}", created.Name);                 // trimmed
        Assert.Equal($"ops-{u}@example.test", created.Email);    // normalised

        var fetched = await manager.GetFromJsonAsync<MerchantView>($"/api/merchants/{created.Id}");
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Theory]
    [InlineData("A", "ok@example.test", "PK")]            // name too short
    [InlineData("Valid Name", "not-an-email", "PK")]      // bad email
    [InlineData("Valid Name", "ok@example.test", "pk")]   // lower-case country
    [InlineData("Valid Name", "ok@example.test", "PAK")]  // wrong length
    public async Task Invalid_input_returns_400_validation_problem(string name, string email, string country)
    {
        var admin = await factory.ClientAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync("/api/merchants",
            new { name, legalName = "Legal Ltd", email, countryCode = country });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_case_insensitively()
    {
        var admin = await factory.ClientAsync(Roles.Admin);
        var first = await CreateMerchantAsync(admin);

        var dup = await admin.PostAsJsonAsync("/api/merchants", new
        {
            name = "Other", legalName = "Other Ltd", email = first.Email.ToUpperInvariant(), countryCode = "PK"
        });

        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Role_based_access_matrix_is_enforced()
    {
        var viewer = await factory.ClientAsync(Roles.Viewer);
        var manager = await factory.ClientAsync(Roles.Manager);
        var admin = await factory.ClientAsync(Roles.Admin);
        var m = await CreateMerchantAsync(manager);

        // Viewer: read only
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/merchants/{m.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/merchants")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/merchants",
            new { name = "Nope", legalName = "Nope Ltd", email = $"v-{Unique()}@example.test", countryCode = "PK" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UpdateMerchantAsync(viewer, m, "Active")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"/api/merchants/{m.Id}")).StatusCode);

        // Manager: read + write, but not delete
        Assert.Equal(HttpStatusCode.OK, (await UpdateMerchantAsync(manager, m, "Active")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync($"/api/merchants/{m.Id}")).StatusCode);

        // Admin: delete
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/merchants/{m.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/merchants/{m.Id}")).StatusCode);
    }

    [Fact]
    public async Task Closed_merchant_cannot_be_reopened()
    {
        var manager = await factory.ClientAsync(Roles.Manager);
        var m = await CreateMerchantAsync(manager);

        Assert.Equal(HttpStatusCode.OK, (await UpdateMerchantAsync(manager, m, "Closed")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await UpdateMerchantAsync(manager, m, "Active")).StatusCode);
    }

    [Fact]
    public async Task Update_cannot_take_another_merchants_email()
    {
        var manager = await factory.ClientAsync(Roles.Manager);
        var a = await CreateMerchantAsync(manager);
        var b = await CreateMerchantAsync(manager);

        var response = await manager.PutAsJsonAsync($"/api/merchants/{b.Id}", new
        {
            name = b.Name, legalName = b.LegalName, email = a.Email, countryCode = b.CountryCode, status = "Active"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_merchant_returns_404_problem_details()
    {
        var admin = await factory.ClientAsync(Roles.Admin);

        var response = await admin.GetAsync($"/api/merchants/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(404, json.GetProperty("status").GetInt32());
        Assert.True(json.TryGetProperty("correlationId", out _));

        // A non-GUID id never reaches the controller.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/merchants/not-a-guid")).StatusCode);
    }

    [Fact]
    public async Task List_supports_paging_sorting_and_filtering()
    {
        var manager = await factory.ClientAsync(Roles.Manager);
        var token = Unique();
        var created = new List<MerchantView>();
        for (var i = 1; i <= 25; i++)
            created.Add(await CreateMerchantAsync(manager, name: $"Pg-{token}-{i:D2}", email: $"pg{i}-{token}@example.test"));

        var page1 = (await manager.GetFromJsonAsync<Paged<MerchantView>>(
            $"/api/merchants?search=Pg-{token}&page=1&pageSize=10&sortBy=name"))!;
        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(10, page1.Items.Count);
        Assert.EndsWith("-01", page1.Items[0].Name);

        var page3 = (await manager.GetFromJsonAsync<Paged<MerchantView>>(
            $"/api/merchants?search=Pg-{token}&page=3&pageSize=10&sortBy=name"))!;
        Assert.Equal(5, page3.Items.Count);
        Assert.EndsWith("-25", page3.Items[^1].Name);

        var desc = (await manager.GetFromJsonAsync<Paged<MerchantView>>(
            $"/api/merchants?search=Pg-{token}&pageSize=5&sortBy=name&desc=true"))!;
        Assert.EndsWith("-25", desc.Items[0].Name);

        await UpdateMerchantAsync(manager, created[3], "Active");
        var active = (await manager.GetFromJsonAsync<Paged<MerchantView>>(
            $"/api/merchants?search=Pg-{token}&status=Active"))!;
        Assert.Equal(1, active.TotalCount);
        Assert.Equal(created[3].Id, active.Items[0].Id);
    }

    [Fact]
    public async Task Invalid_paging_or_sort_parameters_return_400()
    {
        var admin = await factory.ClientAsync(Roles.Admin);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/merchants?sortBy=passwordHash")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/merchants?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/merchants?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/merchants?status=Nonsense")).StatusCode);
    }

    [Fact]
    public async Task Search_treats_like_wildcards_as_literal_text()
    {
        var manager = await factory.ClientAsync(Roles.Manager);
        var token = Unique();
        var literal = await CreateMerchantAsync(manager, name: $"W-{token}-100%");
        await CreateMerchantAsync(manager, name: $"W-{token}-1000");

        var result = (await manager.GetFromJsonAsync<Paged<MerchantView>>(
            $"/api/merchants?search={Uri.EscapeDataString($"{token}-100%")}"))!;

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(literal.Id, result.Items[0].Id);
    }

    [Fact]
    public async Task Correlation_id_is_echoed_or_generated_and_sanitised()
    {
        var client = factory.CreateClient();

        var echoed = new HttpRequestMessage(HttpMethod.Get, "/health");
        echoed.Headers.Add("X-Correlation-Id", "trace-abc_123");
        var r1 = await client.SendAsync(echoed);
        Assert.Equal("trace-abc_123", r1.Headers.GetValues("X-Correlation-Id").Single());

        var generated = await client.GetAsync("/health");
        Assert.False(string.IsNullOrWhiteSpace(generated.Headers.GetValues("X-Correlation-Id").Single()));

        var hostile = new HttpRequestMessage(HttpMethod.Get, "/health");
        hostile.Headers.TryAddWithoutValidation("X-Correlation-Id", "bad value with spaces");
        var r3 = await client.SendAsync(hostile);
        Assert.NotEqual("bad value with spaces", r3.Headers.GetValues("X-Correlation-Id").Single());
    }
}
