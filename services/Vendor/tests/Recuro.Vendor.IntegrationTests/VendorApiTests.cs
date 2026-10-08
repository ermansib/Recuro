using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Vendor.Infrastructure.Persistence;

namespace Recuro.Vendor.IntegrationTests;

public sealed class VendorApiTests(VendorApiFactory api) : IClassFixture<VendorApiFactory>
{
    private static readonly Guid A = VendorApiFactory.TenantA;

    private static readonly object AllGates = new { experience = true, trackRecord = true, agreement = true, nda = true, replacementGuarantee = true, privacyAck = true };
    private static readonly object AllDocuments = new { ndaRef = "docs/nda.pdf", agreementRef = "docs/msa.pdf", privacyAckRef = "docs/dpa.pdf" };

    private Task<HttpResponseMessage> RegisterAsync(
        Guid tenant,
        string? name = null,
        string type = "BgvAgency",
        decimal fee = 6,
        string? justification = null,
        object? gates = null,
        object? documents = null,
        string role = RecuroRoles.HrHead) =>
        api.ClientFor(tenant, role).PostAsJsonAsync("/api/v1/vendors", new
        {
            name = name ?? $"Agency {Guid.NewGuid():N}",
            type,
            fee = new { kind = "PercentOfCtc", value = fee, justification },
            gates = gates ?? AllGates,
            documents = documents ?? AllDocuments,
        });

    private async Task<string> NewVendorIdAsync(Guid tenant, object? gates = null, object? documents = null)
    {
        var response = await RegisterAsync(tenant, gates: gates, documents: documents);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> EmpanelAsync(Guid tenant, string id, string role = RecuroRoles.HrHead) =>
        api.ClientFor(tenant, role).PostAsync($"/api/v1/vendors/{id}/empanel", null);

    private async Task<JsonElement> StatusAsync(Guid tenant, string id, string role = RecuroRoles.Service) =>
        await api.ClientFor(tenant, role).GetFromJsonAsync<JsonElement>($"/api/v1/vendors/{id}/status");

    [Fact]
    public async Task A_registered_vendor_is_pending_until_HR_Head_empanels_it()
    {
        var id = await NewVendorIdAsync(A);

        var pending = await StatusAsync(A, id);
        Assert.Equal("pending", pending.GetProperty("status").GetString());
        Assert.False(pending.GetProperty("active").GetBoolean());

        var empanelled = await EmpanelAsync(A, id);
        Assert.Equal(HttpStatusCode.OK, empanelled.StatusCode);
        var body = await empanelled.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.Equal("R. Iyer", body.GetProperty("approvedBy").GetString());

        var active = await StatusAsync(A, id, RecuroRoles.HrTa);
        Assert.Equal(
            ["active", "name", "status", "type", "vendorId"],
            active.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("active", active.GetProperty("status").GetString());
        Assert.True(active.GetProperty("active").GetBoolean());
        Assert.Equal("BgvAgency", active.GetProperty("type").GetString());
        Assert.Equal(1, await OutboxCountAsync(A, EventTypes.Vendor.Empanelled, id));
    }

    [Fact]
    public async Task Empanelment_is_blocked_while_any_section_15_gate_or_document_is_missing()
    {
        var id = await NewVendorIdAsync(
            A,
            gates: new { experience = true, trackRecord = true, agreement = true, nda = false, replacementGuarantee = true, privacyAck = false },
            documents: new { ndaRef = "docs/nda.pdf" });

        var blocked = await EmpanelAsync(A, id);

        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("empanelment_gates_pending", problem.GetProperty("code").GetString());
        Assert.Equal("Empanelment blocked — 2 of 6 §15 gates pending, 2 document(s) missing.", problem.GetProperty("title").GetString());

        var fixedUp = await api.ClientFor(A, RecuroRoles.HrHead).PutAsJsonAsync($"/api/v1/vendors/{id}/empanelment", new
        {
            fee = new { kind = "PercentOfCtc", value = 7 },
            gates = AllGates,
            documents = AllDocuments,
        });
        Assert.Equal(HttpStatusCode.OK, fixedUp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await EmpanelAsync(A, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await EmpanelAsync(A, id)).StatusCode);
    }

    [Fact]
    public async Task Out_of_band_fees_need_a_justification()
    {
        var refused = await RegisterAsync(A, fee: 12);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("fee.justification", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var accepted = await RegisterAsync(A, fee: 12, justification: "Niche KMP search; board approved premium");
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.False((await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("feeTerms").GetProperty("withinBand").GetBoolean());
    }

    [Fact]
    public async Task Names_are_unique_per_tenant_regardless_of_case()
    {
        var name = $"Unique {Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(A, name)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await RegisterAsync(A, name.ToUpperInvariant())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(VendorApiFactory.TenantB, name)).StatusCode);
    }

    [Fact]
    public async Task De_empanelling_switches_the_vendor_off_and_tells_Bgv()
    {
        var id = await NewVendorIdAsync(A);
        await EmpanelAsync(A, id);

        var noReason = await api.ClientFor(A, RecuroRoles.HrHead).PostAsJsonAsync($"/api/v1/vendors/{id}/de-empanel", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var off = await api.ClientFor(A, RecuroRoles.HrHead).PostAsJsonAsync($"/api/v1/vendors/{id}/de-empanel", new { reason = "Repeated SLA breaches in Q3" });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Equal("off", (await StatusAsync(A, id)).GetProperty("status").GetString());
        Assert.Equal(1, await OutboxCountAsync(A, EventTypes.Vendor.DeEmpanelled, id));
    }

    [Fact]
    public async Task Lists_filter_by_type_and_status()
    {
        var tenant = Guid.NewGuid();
        var agency = await NewVendorIdAsync(tenant);
        await EmpanelAsync(tenant, agency);
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(tenant, type: "Consultant")).StatusCode);

        var all = await api.ClientFor(tenant, RecuroRoles.MdCeo).GetFromJsonAsync<JsonElement>("/api/v1/vendors");
        Assert.Equal(2, all.GetArrayLength());
        var active = await api.ClientFor(tenant, RecuroRoles.HrTa).GetFromJsonAsync<JsonElement>("/api/v1/vendors?type=BgvAgency&status=Active");
        Assert.Equal([agency], active.EnumerateArray().Select(v => v.GetProperty("id").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.ClientFor(tenant, RecuroRoles.HrTa).GetAsync("/api/v1/vendors?type=Caterer")).StatusCode);
    }

    [Fact]
    public async Task Only_HR_Head_manages_vendors()
    {
        var id = await NewVendorIdAsync(A);

        Assert.Equal(HttpStatusCode.Forbidden, (await RegisterAsync(A, role: RecuroRoles.HrTa)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await EmpanelAsync(A, id, RecuroRoles.MdCeo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(A, RecuroRoles.Employee).GetAsync("/api/v1/vendors")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.ClientFor(A, RecuroRoles.Employee).GetAsync($"/api/v1/vendors/{id}/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"/api/v1/vendors/{id}/status")).StatusCode);
    }

    [Fact]
    public async Task Unknown_or_other_tenants_vendors_are_404()
    {
        var id = await NewVendorIdAsync(A);

        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor(VendorApiFactory.TenantB, RecuroRoles.Service).GetAsync($"/api/v1/vendors/{id}/status")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await EmpanelAsync(VendorApiFactory.TenantB, id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor(A, RecuroRoles.Service).GetAsync("/api/v1/vendors/not-a-guid/status")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor(A, RecuroRoles.Service).GetAsync($"/api/v1/vendors/{Guid.NewGuid()}/status")).StatusCode);
    }

    private async Task<int> OutboxCountAsync(Guid tenant, string type, string vendorId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenant);
        var db = scope.ServiceProvider.GetRequiredService<VendorDbContext>();
        var envelopes = await db.OutboxMessages.Where(m => m.TenantId == tenant && m.Type == type).Select(m => m.Envelope).ToListAsync();
        return envelopes.Count(e => e.Contains(vendorId, StringComparison.Ordinal));
    }
}
