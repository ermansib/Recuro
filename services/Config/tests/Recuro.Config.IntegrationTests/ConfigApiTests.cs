using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Config.Infrastructure.Persistence;

namespace Recuro.Config.IntegrationTests;

public sealed class ConfigApiTests(ConfigApiFactory api) : IClassFixture<ConfigApiFactory>
{
    private HttpClient HrTa(Guid? tenant = null) => api.ClientFor(tenant ?? ConfigApiFactory.TenantA, RecuroRoles.HrTa, "u-hrta", "Riya Sharma");

    private HttpClient HrHead(Guid? tenant = null) => api.ClientFor(tenant ?? ConfigApiFactory.TenantA, RecuroRoles.HrHead, "u-head", "Kavya Iyer");

    private HttpClient Service() => api.ClientFor(ConfigApiFactory.TenantA, RecuroRoles.Service, "svc-workflow", "workflow");

    [Fact]
    public async Task Resolving_DOA_returns_the_contract_shape_with_the_version()
    {
        var route = await Service().GetFromJsonAsync<JsonElement>("/api/v1/resolve/doa?grade=M3");

        // services/docs/architecture.md "Synchronous contracts": the frontend DoaRoute plus version and legs.
        Assert.Equal(
            ["approverRole", "approving", "bandLabel", "budgetStatus", "configVersionId", "grade", "initiating", "legs", "overallTat", "recommending"],
            route.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("HR Head", route.GetProperty("approving").GetString());
        Assert.Equal("in", route.GetProperty("budgetStatus").GetString());
        Assert.Equal(35, route.GetProperty("overallTat").GetProperty("maxDays").GetInt32());
        var leg = route.GetProperty("legs")[0];
        Assert.Equal(["assignees", "escalation", "name", "slaWorkingDays"], leg.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["afterWorkingDays", "label", "role"], leg.GetProperty("escalation")[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Out_of_budget_routes_have_the_extra_leg()
    {
        var inBudget = await Service().GetFromJsonAsync<JsonElement>("/api/v1/resolve/doa?grade=E&budget=in");
        var oob = await Service().GetFromJsonAsync<JsonElement>("/api/v1/resolve/doa?grade=E&budget=oob");

        Assert.Equal("oob", oob.GetProperty("budgetStatus").GetString());
        Assert.Equal(inBudget.GetProperty("legs").GetArrayLength() + 1, oob.GetProperty("legs").GetArrayLength());
    }

    [Fact]
    public async Task An_unknown_grade_is_a_404_with_a_hint_and_a_missing_one_a_400()
    {
        var unknown = await Service().GetAsync("/api/v1/resolve/doa?grade=Z9");
        var missing = await Service().GetAsync("/api/v1/resolve/doa");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var problem = await unknown.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unknown_grade", problem.GetProperty("code").GetString());
        Assert.Contains("KMP", problem.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task Working_days_use_the_business_calendar()
    {
        // Wed 30 Sep 2026 + 3 working days: Thu 1, (Fri 2 Oct holiday, weekend), Mon 5, Tue 6.
        var result = await Service().GetFromJsonAsync<JsonElement>("/api/v1/resolve/working-days?from=2026-09-30&days=3");

        Assert.Equal("2026-10-06", result.GetProperty("date").GetString());
        Assert.True(result.TryGetProperty("configVersionId", out _));
    }

    [Fact]
    public async Task Negative_working_days_count_backwards()
    {
        var result = await Service().GetFromJsonAsync<JsonElement>("/api/v1/resolve/working-days?from=2026-10-06&days=-3");

        Assert.Equal("2026-09-30", result.GetProperty("date").GetString());
    }

    [Fact]
    public async Task The_onboarding_matrix_resolves_with_the_seed()
    {
        var result = await Service().GetFromJsonAsync<JsonElement>("/api/v1/resolve/matrices/onboarding");

        Assert.Equal("onboarding", result.GetProperty("matrixType").GetString());
        var content = result.GetProperty("content");
        Assert.Equal(11, content.GetProperty("checklist").GetArrayLength());
        Assert.Equal("bgv-report", content.GetProperty("checklist")[1].GetProperty("key").GetString());
        Assert.True(content.GetProperty("documents")[0].GetProperty("mandatory").GetBoolean());
        Assert.Equal(5, content.GetProperty("provisioningWorkingDaysBefore").GetInt32());
    }

    [Fact]
    public async Task An_unknown_location_is_a_404()
    {
        var response = await Service().GetAsync("/api/v1/resolve/working-days?from=2026-09-30&days=3&location=mars");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_change_needs_a_second_approver_and_running_workflows_keep_their_pinned_version()
    {
        var pinned = (await Service().GetFromJsonAsync<JsonElement>("/api/v1/resolve/doa?grade=KMP")).GetProperty("configVersionId").GetString();
        var content = await CurrentDoaContentAsync();
        content["routes"]![4]!["approving"] = "Board (full)";
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(30);

        var proposed = await HrTa().PostAsJsonAsync("/api/v1/config/doa/versions", new { effectiveFrom, content, note = "Board approves KMP" });
        Assert.Equal(HttpStatusCode.Created, proposed.StatusCode);
        var draft = await proposed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        var id = draft.GetProperty("id").GetString();

        var selfApproval = await api.ClientFor(ConfigApiFactory.TenantA, RecuroRoles.HrHead, "u-hrta").PostAsync($"/api/v1/config/doa/versions/{id}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);

        var approved = await HrHead().PostAsync($"/api/v1/config/doa/versions/{id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("Active", (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(1, await OutboxCountAsync("config.version.activated.v1", id!));

        // Before its effective date, and for anyone pinned to the old version, nothing changes (CFG-003).
        var now = await Service().GetFromJsonAsync<JsonElement>("/api/v1/resolve/doa?grade=KMP");
        var stillPinned = await Service().GetFromJsonAsync<JsonElement>($"/api/v1/resolve/doa?grade=KMP&versionId={pinned}");
        var later = await Service().GetFromJsonAsync<JsonElement>($"/api/v1/resolve/doa?grade=KMP&at={Uri.EscapeDataString(effectiveFrom.AddDays(1).ToString("O"))}");
        Assert.Equal(pinned, now.GetProperty("configVersionId").GetString());
        Assert.Equal("Board / NRC", stillPinned.GetProperty("approving").GetString());
        Assert.Equal("Board (full)", later.GetProperty("approving").GetString());
        Assert.Equal(id, later.GetProperty("configVersionId").GetString());
    }

    [Fact]
    public async Task An_active_version_cannot_be_edited()
    {
        var versions = await HrTa().GetFromJsonAsync<JsonElement>("/api/v1/config/tat/versions");
        var active = versions.EnumerateArray().First(v => v.GetProperty("status").GetString() == "Active");
        var id = active.GetProperty("id").GetString();

        var response = await HrTa().PutAsJsonAsync($"/api/v1/config/tat/versions/{id}", new { effectiveFrom = DateTimeOffset.UtcNow, content = active.GetProperty("content") });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_rejection_needs_a_reason()
    {
        var content = await CurrentDoaContentAsync();
        var draft = await (await HrTa().PostAsJsonAsync("/api/v1/config/doa/versions", new { effectiveFrom = DateTimeOffset.UtcNow, content }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = draft.GetProperty("id").GetString();

        var noReason = await HrHead().PostAsJsonAsync($"/api/v1/config/doa/versions/{id}/reject", new { reason = "no" });
        var rejected = await HrHead().PostAsJsonAsync($"/api/v1/config/doa/versions/{id}/reject", new { reason = "Band labels need the 2027 revision" });

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal("Rejected", (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Invalid_matrices_are_400s_that_name_the_field()
    {
        var content = await CurrentDoaContentAsync();
        content["routes"]![0]!["approverRole"] = "boss";

        var response = await HrTa().PostAsJsonAsync("/api/v1/config/doa/versions", new { effectiveFrom = DateTimeOffset.UtcNow, content });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(problem.GetProperty("errors").EnumerateArray(), e => e.GetProperty("field").GetString() == "content.routes[0].approverRole");
    }

    [Fact]
    public async Task People_outside_HR_cannot_change_or_read_rules()
    {
        var candidate = api.ClientFor(ConfigApiFactory.TenantA, RecuroRoles.Candidate);
        var mdCeo = api.ClientFor(ConfigApiFactory.TenantA, RecuroRoles.MdCeo);

        Assert.Equal(HttpStatusCode.Forbidden, (await candidate.GetAsync("/api/v1/resolve/doa?grade=E")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mdCeo.PostAsJsonAsync("/api/v1/config/doa/versions", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/v1/resolve/doa?grade=E")).StatusCode);
    }

    [Fact]
    public async Task Another_tenant_never_sees_these_rules()
    {
        var versions = await HrHead(ConfigApiFactory.TenantB).GetFromJsonAsync<JsonElement>("/api/v1/config/doa/versions");
        var resolve = await HrHead(ConfigApiFactory.TenantB).GetAsync("/api/v1/resolve/doa?grade=E");

        Assert.Empty(versions.EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, resolve.StatusCode);
    }

    [Fact]
    public async Task The_frontend_rules_slice_matches_RuleConfig()
    {
        var rules = await HrTa().GetFromJsonAsync<JsonElement>("/api/v1/config/rules");

        Assert.Equal(["bgvChecks", "doa", "effectiveFrom", "offerMatrix", "version"], rules.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["approverRole", "approving", "bandLabel", "grade", "initiating", "overallTat", "recommending"],
            rules.GetProperty("doa")[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["deviation", "levels", "withinBand"], rules.GetProperty("offerMatrix")[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(9, rules.GetProperty("bgvChecks").GetArrayLength());
    }

    [Fact]
    public async Task Any_matrix_resolves_as_a_whole()
    {
        var offer = await Service().GetFromJsonAsync<JsonElement>("/api/v1/resolve/matrices/offer");

        Assert.Equal("offer", offer.GetProperty("matrixType").GetString());
        Assert.Equal(3, offer.GetProperty("content").GetProperty("rules").GetArrayLength());
    }

    [Fact]
    public async Task An_unknown_matrix_type_is_a_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await HrTa().GetAsync("/api/v1/config/payroll/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await HrTa().GetAsync("/api/v1/resolve/matrices/payroll")).StatusCode);
    }

    [Fact]
    public async Task The_database_refuses_changes_to_an_active_version()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConfigDbContext>();

        var update = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("UPDATE rule_set_versions SET note = 'edited' WHERE status = 'Active'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM rule_set_versions WHERE status = 'Active'"));

        Assert.Contains("immutable", update.MessageText, StringComparison.Ordinal);
        Assert.Contains("immutable", delete.MessageText, StringComparison.Ordinal);
    }

    private async Task<JsonNode> CurrentDoaContentAsync()
    {
        var versions = await HrTa().GetFromJsonAsync<JsonElement>("/api/v1/config/doa/versions");
        var active = versions.EnumerateArray().First(v => v.GetProperty("status").GetString() == "Active");
        return JsonNode.Parse(active.GetProperty("content").GetRawText())!;
    }

    private async Task<int> OutboxCountAsync(string type, string versionId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(ConfigApiFactory.TenantA);
        var db = scope.ServiceProvider.GetRequiredService<ConfigDbContext>();
        var envelopes = await db.OutboxMessages.Where(m => m.Type == type).Select(m => m.Envelope).ToListAsync();
        return envelopes.Count(e => e.Contains(versionId, StringComparison.Ordinal));
    }
}
