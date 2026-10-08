using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.Bgv.Infrastructure.Persistence;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Bgv.IntegrationTests;

public sealed class BgvApiTests(BgvApiFactory api) : IClassFixture<BgvApiFactory>
{
    private static readonly Guid A = BgvApiFactory.TenantA;

    private static string NewAppId() => $"APP-T-{Guid.NewGuid():N}"[..24];

    /// <summary>Pipeline reports the application at BGV, as <c>pipeline.stage.changed</c> would.</summary>
    private async Task<string> AtBgvAsync(Guid tenant, string source = "Portal")
    {
        var appId = NewAppId();
        await ProcessAsync(tenant, EventTypes.Pipeline.StageChanged, new { appId, reqId = "MRF-2026-0142", candidateId = "c-1", source, from = "Selection", to = "BGV", at = DateTimeOffset.UtcNow });
        return appId;
    }

    private Task<HttpResponseMessage> InitiateAsync(Guid tenant, string appId, string vendorId = StubServices.ActiveAgency, bool consent = true, string role = RecuroRoles.HrTa, string grade = "M3", string[]? flags = null) =>
        api.ClientFor(tenant, role).PostAsJsonAsync("/api/v1/bgv/cases", new
        {
            appId,
            vendorId,
            vendorCaseRef = "AB-88213",
            consent = consent ? new { at = "2026-10-01T10:00:00Z", textVersion = "v1", source = "careers-portal" } : null,
            grade,
            roleFlags = flags ?? ["customerFacing"],
        });

    private async Task<JsonElement> NewCaseAsync(Guid tenant, string? appId = null)
    {
        var response = await InitiateAsync(tenant, appId ?? await AtBgvAsync(tenant));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<HttpResponseMessage> UpdateCheckAsync(Guid tenant, string caseRef, string type, string status, string? sensitiveNote = null) =>
        api.ClientFor(tenant, RecuroRoles.HrTa).PostAsJsonAsync($"/api/v1/bgv/cases/{caseRef}/checks/{type}", new { status, note = $"{type} {status}", sensitiveNote });

    private async Task ClearAllAsync(Guid tenant, JsonElement bgvCase)
    {
        var appId = bgvCase.GetProperty("appId").GetString()!;
        foreach (var check in bgvCase.GetProperty("checks").EnumerateArray().Where(c => c.GetProperty("status").GetString() == "Pending"))
        {
            var type = check.GetProperty("type").GetString()!;
            Assert.Equal(HttpStatusCode.OK, (await UpdateCheckAsync(tenant, appId, type, "InProgress")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await UpdateCheckAsync(tenant, appId, type, "Cleared")).StatusCode);
        }
    }

    [Fact]
    public async Task HR_TA_initiates_a_case_in_the_frontend_BgvCase_shape()
    {
        var bgvCase = await NewCaseAsync(A);

        Assert.Equal(
            ["appId", "checks", "consentAt", "id", "initiatedAt", "needsReassignment", "status", "tatDay", "tatTotal", "vendor", "vendorCaseRef", "vendorId"],
            bgvCase.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("AuthBridge", bgvCase.GetProperty("vendor").GetString());
        Assert.Equal(12, bgvCase.GetProperty("tatTotal").GetInt32());
        Assert.Equal(0, bgvCase.GetProperty("tatDay").GetInt32());
        var checks = bgvCase.GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("type").GetString()!);
        Assert.Equal(
            ["date", "detail", "label", "note", "status", "type"],
            checks["identity"].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Pending", checks["police"].GetProperty("status").GetString());
        Assert.Equal("NotApplicable", checks["fitProper"].GetProperty("status").GetString());
        Assert.StartsWith("Not applicable (M3)", checks["fitProper"].GetProperty("note").GetString(), StringComparison.Ordinal);

        var appId = bgvCase.GetProperty("appId").GetString()!;
        Assert.Equal(1, await OutboxCountAsync(A, EventTypes.Bgv.CaseInitiated, appId));
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(A);
        var stored = await scope.ServiceProvider.GetRequiredService<BgvDbContext>().Cases.SingleAsync(c => c.AppId == appId);
        Assert.Equal("cfg-bgv-7", stored.MatrixVersionId);
    }

    [Fact]
    public async Task Initiation_needs_consent_an_active_BGV_agency_and_the_BGV_stage()
    {
        var appId = await AtBgvAsync(A);

        var noConsent = await InitiateAsync(A, appId, consent: false);
        Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);
        Assert.Contains("consent_required", await noConsent.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        foreach (var vendor in new[] { StubServices.OffAgency, StubServices.Consultant, "no-such-vendor" })
        {
            var refused = await InitiateAsync(A, appId, vendor);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("vendor_not_active", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Equal(HttpStatusCode.Conflict, (await InitiateAsync(A, NewAppId())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await InitiateAsync(A, appId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await InitiateAsync(A, appId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await InitiateAsync(A, await AtBgvAsync(A), grade: "Z9")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await InitiateAsync(A, await AtBgvAsync(A), flags: ["astrology"])).StatusCode);
    }

    [Fact]
    public async Task Internal_candidates_default_to_a_delta_only_scope()
    {
        var bgvCase = await NewCaseAsync(A, await AtBgvAsync(A, source: "IJP"));

        var checks = bgvCase.GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("type").GetString()!, c => c.GetProperty("status").GetString());
        Assert.Equal("NotApplicable", checks["identity"]);
        Assert.Equal("NotApplicable", checks["employment"]);
        Assert.Equal("Pending", checks["coi"]);
    }

    [Fact]
    public async Task The_release_gate_lists_blockers_until_every_check_clears_then_bgv_cleared_fires_once()
    {
        var bgvCase = await NewCaseAsync(A);
        var appId = bgvCase.GetProperty("appId").GetString()!;
        var caseId = bgvCase.GetProperty("id").GetString()!;

        var blocked = await api.ClientFor(A, RecuroRoles.HrTa).PostAsync($"/api/v1/bgv/cases/{caseId}/release", null);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("bgv_release_blocked", problem.GetProperty("code").GetString());
        Assert.StartsWith("Cannot release — 0 in progress, 4 pending, 0 flagged", problem.GetProperty("title").GetString(), StringComparison.Ordinal);

        var gate = await api.ClientFor(A, RecuroRoles.Service).GetFromJsonAsync<JsonElement>($"/api/v1/bgv/cases/{appId}/release-gate");
        Assert.False(gate.GetProperty("cleared").GetBoolean());
        Assert.Equal(4, gate.GetProperty("blockers").GetArrayLength());

        Assert.Equal(HttpStatusCode.Conflict, (await UpdateCheckAsync(A, appId, "identity", "Cleared")).StatusCode);
        await ClearAllAsync(A, bgvCase);

        gate = await api.ClientFor(A, RecuroRoles.Service).GetFromJsonAsync<JsonElement>($"/api/v1/bgv/cases/{appId}/release-gate");
        Assert.True(gate.GetProperty("cleared").GetBoolean());
        Assert.Equal(0, gate.GetProperty("blockers").GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await api.ClientFor(A, RecuroRoles.HrHead).PostAsync($"/api/v1/bgv/cases/{appId}/release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.ClientFor(A, RecuroRoles.HrTa).PostAsync($"/api/v1/bgv/cases/{appId}/release", null)).StatusCode);
        Assert.Equal(1, await OutboxCountAsync(A, EventTypes.Bgv.Cleared, appId));
        Assert.Equal(8, await OutboxCountAsync(A, EventTypes.Bgv.CheckUpdated, appId));
    }

    [Fact]
    public async Task An_adverse_finding_holds_the_offer_and_opens_the_escalation_workflow()
    {
        var bgvCase = await NewCaseAsync(A);
        var appId = bgvCase.GetProperty("appId").GetString()!;
        var caseId = bgvCase.GetProperty("id").GetString()!;
        api.Stubs.WorkflowStarts.Clear();

        var flagged = await api.ClientFor(A, RecuroRoles.HrTa).PostAsJsonAsync($"/api/v1/bgv/cases/{caseId}/adverse", new
        {
            check = "police",
            description = "Pending FIR at local station",
            action = "HoldAndEscalate",
        });

        Assert.Equal(HttpStatusCode.OK, flagged.StatusCode);
        var body = await flagged.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UnderReview", body.GetProperty("status").GetString());
        Assert.Equal("police", body.GetProperty("adverse").GetProperty("check").GetString());
        Assert.Equal("HoldAndEscalate", body.GetProperty("adverse").GetProperty("action").GetString());
        Assert.Equal(1, await OutboxCountAsync(A, EventTypes.Bgv.AdverseFlagged, appId));

        var start = Assert.Single(api.Stubs.WorkflowStarts);
        Assert.Equal("bgv-adverse", start.GetProperty("type").GetString());
        Assert.Equal(caseId, start.GetProperty("subject").GetProperty("id").GetString());
        Assert.Equal("cfg-escalation-7", start.GetProperty("configVersionId").GetString());
        Assert.Equal(["hrhead", "mdceo"], start.GetProperty("legs").EnumerateArray().Select(l => l.GetProperty("assignees")[0].GetProperty("role").GetString()));
        Assert.Equal(["escalate", "clarify", "rescind", "override"], start.GetProperty("presentation").GetProperty("actions").EnumerateArray().Select(a => a.GetProperty("id").GetString()));

        var release = await api.ClientFor(A, RecuroRoles.HrTa).PostAsync($"/api/v1/bgv/cases/{appId}/release", null);
        Assert.Equal(HttpStatusCode.Conflict, release.StatusCode);

        // MD/CEO overrides in the approvals inbox; Workflow reports the decision.
        await ProcessAsync(A, EventTypes.Workflow.TaskCompleted, new
        {
            type = "bgv-adverse",
            subjectType = "BgvCase",
            subjectId = caseId,
            actionId = "override",
            reason = "FIR closed; court order on file",
            instanceStatus = "Rejected",
        });

        var after = await GetCaseAsync(A, appId, RecuroRoles.HrTa);
        Assert.Equal("Open", after.GetProperty("status").GetString());
        Assert.False(after.TryGetProperty("adverse", out _));
        Assert.Equal("Override", after.GetProperty("resolution").GetProperty("outcome").GetString());
        var police = after.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("type").GetString() == "police");
        Assert.Equal("Cleared", police.GetProperty("status").GetString());
        Assert.Equal(1, await OutboxCountAsync(A, EventTypes.Bgv.Resolved, appId));
    }

    [Fact]
    public async Task Rescind_closes_the_case_and_redelivery_changes_nothing()
    {
        var bgvCase = await NewCaseAsync(A);
        var appId = bgvCase.GetProperty("appId").GetString()!;
        var caseId = bgvCase.GetProperty("id").GetString()!;
        await api.ClientFor(A, RecuroRoles.HrTa).PostAsJsonAsync($"/api/v1/bgv/cases/{appId}/adverse", new { check = "employment", description = "Fake experience letter", action = "SeekClarification" });

        var decision = new { type = "bgv-adverse", subjectType = "BgvCase", subjectId = caseId, actionId = "rescind", reason = "Forged document confirmed by vendor", instanceStatus = "Rejected" };
        await ProcessAsync(A, EventTypes.Workflow.TaskCompleted, decision);
        await ProcessAsync(A, EventTypes.Workflow.TaskCompleted, decision);

        var after = await GetCaseAsync(A, appId, RecuroRoles.HrTa);
        Assert.Equal("Rescinded", after.GetProperty("status").GetString());
        Assert.Equal(1, await OutboxCountAsync(A, EventTypes.Bgv.Resolved, appId));
        Assert.Equal(HttpStatusCode.Conflict, (await api.ClientFor(A, RecuroRoles.HrTa).PostAsync($"/api/v1/bgv/cases/{appId}/release", null)).StatusCode);
    }

    [Fact]
    public async Task Sensitive_notes_follow_the_masking_map_and_fall_back_to_HR_only()
    {
        var bgvCase = await NewCaseAsync(A);
        var appId = bgvCase.GetProperty("appId").GetString()!;
        await UpdateCheckAsync(A, appId, "employment", "InProgress");
        await UpdateCheckAsync(A, appId, "employment", "Cleared", sensitiveNote: "last drawn ₹18.2L ✓");

        static string? Note(JsonElement c) => c.GetProperty("checks").EnumerateArray().Single(x => x.GetProperty("type").GetString() == "employment")
            .TryGetProperty("sensitiveNote", out var n) ? n.GetString() : null;

        Assert.Equal("last drawn ₹18.2L ✓", Note(await GetCaseAsync(A, appId, RecuroRoles.HrHead)));
        Assert.Null(Note(await GetCaseAsync(A, appId, RecuroRoles.MdCeo)));

        // Identity has no bgvCheck map yet (404): HR roles see it, MD/CEO still doesn't.
        api.Stubs.RolesHidingSensitive = null;
        try
        {
            Assert.Equal("last drawn ₹18.2L ✓", Note(await GetCaseAsync(A, appId, RecuroRoles.HrTa)));
            Assert.Null(Note(await GetCaseAsync(A, appId, RecuroRoles.MdCeo)));
        }
        finally
        {
            api.Stubs.RolesHidingSensitive = ["mdceo", "service"];
        }
    }

    [Fact]
    public async Task Roles_follow_the_RBAC_matrix()
    {
        var appId = await AtBgvAsync(A);

        Assert.Equal(HttpStatusCode.Forbidden, (await InitiateAsync(A, appId, role: RecuroRoles.HrHead)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await InitiateAsync(A, appId, role: RecuroRoles.MdCeo)).StatusCode);
        var bgvCase = await NewCaseAsync(A, appId);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(A, RecuroRoles.MdCeo).PostAsync($"/api/v1/bgv/cases/{appId}/release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(A, RecuroRoles.Employee).GetAsync($"/api/v1/bgv/cases/{appId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"/api/v1/bgv/cases/{appId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.ClientFor(A, RecuroRoles.MdCeo).GetAsync($"/api/v1/bgv/cases/{bgvCase.GetProperty("id").GetString()}")).StatusCode);
    }

    [Fact]
    public async Task Tenant_B_cannot_read_or_change_tenant_A_cases()
    {
        var bgvCase = await NewCaseAsync(A);
        var appId = bgvCase.GetProperty("appId").GetString()!;
        var b = BgvApiFactory.TenantB;

        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor(b, RecuroRoles.HrTa).GetAsync($"/api/v1/bgv/cases/{appId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UpdateCheckAsync(b, appId, "identity", "InProgress")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await InitiateAsync(b, appId)).StatusCode);
    }

    [Fact]
    public async Task A_de_empanelled_vendors_open_cases_are_queued_and_reassigned()
    {
        var tenant = Guid.NewGuid();
        var first = await NewCaseAsync(tenant);
        var second = await NewCaseAsync(tenant);
        await ClearAllAsync(tenant, second);

        await ProcessAsync(tenant, EventTypes.Vendor.DeEmpanelled, new { vendorId = StubServices.ActiveAgency, reason = "SLA", reassignmentHint = "reassign-open-cases" });

        var queue = await api.ClientFor(tenant, RecuroRoles.HrHead).GetFromJsonAsync<JsonElement>("/api/v1/bgv/cases/reassignment");
        Assert.Equal([first.GetProperty("appId").GetString()], queue.EnumerateArray().Select(c => c.GetProperty("appId").GetString()));

        var refused = await api.ClientFor(tenant, RecuroRoles.HrHead).PostAsJsonAsync("/api/v1/bgv/cases/reassign", new { fromVendorId = StubServices.ActiveAgency, toVendorId = StubServices.OffAgency });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var moved = await api.ClientFor(tenant, RecuroRoles.HrHead).PostAsJsonAsync("/api/v1/bgv/cases/reassign", new { fromVendorId = StubServices.ActiveAgency, toVendorId = StubServices.OtherAgency });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Single((await moved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("appIds").EnumerateArray());

        var after = await GetCaseAsync(tenant, first.GetProperty("appId").GetString()!, RecuroRoles.HrTa);
        Assert.Equal("VerifyPro", after.GetProperty("vendor").GetString());
        Assert.False(after.GetProperty("needsReassignment").GetBoolean());
        Assert.Equal(0, (await api.ClientFor(tenant, RecuroRoles.HrHead).GetFromJsonAsync<JsonElement>("/api/v1/bgv/cases/reassignment")).GetArrayLength());
    }

    [Fact]
    public async Task A_rejected_application_cancels_its_running_case()
    {
        var bgvCase = await NewCaseAsync(A);
        var appId = bgvCase.GetProperty("appId").GetString()!;

        await ProcessAsync(A, EventTypes.Pipeline.StageChanged, new { appId, from = "BGV", to = "Rejected", at = DateTimeOffset.UtcNow.AddSeconds(5) });

        Assert.Equal("Cancelled", (await GetCaseAsync(A, appId, RecuroRoles.HrTa)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_dashboard_tile_counts_running_cases_and_those_due_soon()
    {
        var tenant = Guid.NewGuid();
        await NewCaseAsync(tenant);
        var cleared = await NewCaseAsync(tenant);
        await ClearAllAsync(tenant, cleared);

        var fragment = await api.ClientFor(tenant, RecuroRoles.HrTa).GetFromJsonAsync<JsonElement>("/api/v1/bgv/dashboard/ta");

        var tile = Assert.Single(fragment.GetProperty("stats").EnumerateArray());
        Assert.Equal("BGV in Progress", tile.GetProperty("label").GetString());
        Assert.Equal("1", tile.GetProperty("value").GetString());
        Assert.Equal("None due soon", tile.GetProperty("trend").GetString());
        Assert.Equal("a", tile.GetProperty("tone").GetString());
    }

    [Fact]
    public async Task Without_Config_and_nothing_cached_initiation_is_503_not_a_guess()
    {
        var tenant = Guid.NewGuid();
        var appId = await AtBgvAsync(tenant);
        api.Stubs.ConfigDown = true;
        try
        {
            var response = await InitiateAsync(tenant, appId);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            api.Stubs.ConfigDown = false;
        }
    }

    [Fact]
    public async Task Calls_to_other_services_carry_the_callers_credentials()
    {
        var appId = await AtBgvAsync(A);
        api.Stubs.ForwardedRoles.Clear();

        await InitiateAsync(A, appId);

        Assert.NotEmpty(api.Stubs.ForwardedRoles);
        Assert.All(api.Stubs.ForwardedRoles, role => Assert.Equal(RecuroRoles.HrTa, role));
    }

    private async Task<JsonElement> GetCaseAsync(Guid tenant, string caseRef, string role) =>
        await api.ClientFor(tenant, role).GetFromJsonAsync<JsonElement>($"/api/v1/bgv/cases/{caseRef}");

    private async Task<int> OutboxCountAsync(Guid tenant, string type, string appId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenant);
        var db = scope.ServiceProvider.GetRequiredService<BgvDbContext>();
        var envelopes = await db.OutboxMessages.Where(m => m.TenantId == tenant && m.Type == type).Select(m => m.Envelope).ToListAsync();
        return envelopes.Count(e => e.Contains($"\"{appId}\"", StringComparison.Ordinal));
    }

    private Task ProcessAsync(Guid tenant, string type, object data) =>
        api.Services.GetRequiredService<IntegrationEventProcessor>().ProcessAsync(Event(tenant, type, data), CancellationToken.None);

    private static CloudEvent Event(Guid tenant, string type, object data) => new()
    {
        Id = Guid.CreateVersion7(),
        Source = "/services/test",
        Type = type,
        Subject = "test",
        Time = DateTimeOffset.UtcNow,
        TenantId = tenant,
        ActorId = "u-9",
        ActorName = "V. Rao",
        ActorRole = RecuroRoles.MdCeo,
        Data = JsonSerializer.SerializeToElement(data, EventJson.Options),
    };
}
