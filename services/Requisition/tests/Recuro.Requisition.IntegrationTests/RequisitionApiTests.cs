using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Requisition.Infrastructure.Persistence;

namespace Recuro.Requisition.IntegrationTests;

public sealed class RequisitionApiTests(RequisitionApiFactory api) : IClassFixture<RequisitionApiFactory>
{
    private const string Requisitions = "/api/v1/requisitions";

    private static object Input(string designation = "Senior Manager — Credit", string grade = "M3", bool outOfBudget = false) => new
    {
        department = "Credit & Risk",
        designation,
        grade,
        location = "HQ — Mumbai",
        positions = 1,
        reportingManager = "N. Sharma — DVP, Credit",
        employmentType = "Permanent",
        nature = "NewPosition",
        replacementReason = "",
        joiningDate = "2026-12-01",
        band = "₹18L – ₹24L",
        outOfBudget,
        oobJustification = outOfBudget ? "Board-approved expansion of the Pune hub." : "",
        qualifications = "CA / MBA-Finance",
        sourcingChannels = new[] { "Employee Referral", "Job Portals" },
    };

    private HttpClient HrTa(Guid? tenant = null) => api.ClientFor(tenant ?? RequisitionApiFactory.TenantA, RecuroRoles.HrTa);

    private async Task<JsonElement> RaiseAsync(object? input = null, Guid? tenant = null)
    {
        var response = await HrTa(tenant).PostAsJsonAsync(Requisitions, input ?? Input());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task CompleteWorkflowAsync(string reqId, string status, string? reason = null)
    {
        var cloudEvent = new CloudEvent
        {
            Id = Guid.CreateVersion7(),
            Source = "/services/workflow",
            Type = "workflow.task.completed.v1",
            Subject = $"Task/{Guid.NewGuid()}",
            Time = DateTimeOffset.UtcNow,
            TenantId = RequisitionApiFactory.TenantA,
            ActorId = "u-9",
            ActorName = "K. Mehta",
            ActorRole = RecuroRoles.HrHead,
            Data = JsonSerializer.SerializeToElement(new
            {
                taskId = Guid.NewGuid(),
                type = "MRF",
                subjectType = "Requisition",
                subjectId = reqId,
                decision = status == "Approved" ? "approve" : "reject",
                reason,
                instanceStatus = status,
            }),
        };
        var processor = api.Services.GetRequiredService<IntegrationEventProcessor>();
        await processor.ProcessAsync(cloudEvent, CancellationToken.None);
        await processor.ProcessAsync(cloudEvent, CancellationToken.None);
    }

    private async Task<List<string>> OutboxTypesAsync(string reqId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RequisitionDbContext>();
        var subject = $"\"subject\":\"Requisition/{reqId}\"";
        var rows = await db.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).ToListAsync();
        return rows.Where(r => r.Envelope.Replace(" ", string.Empty, StringComparison.Ordinal).Contains(subject, StringComparison.Ordinal)).Select(r => r.Type).ToList();
    }

    [Fact]
    public async Task HR_TA_raises_an_MRF_in_the_frontend_shape_and_it_goes_for_approval()
    {
        var req = await RaiseAsync();

        Assert.Equal(
            [
                "ageDays", "band", "department", "designation", "employmentType", "grade", "joiningDate", "location", "nature",
                "oobJustification", "outOfBudget", "owner", "positions", "qualifications", "raisedAt", "replacementReason",
                "reportingManager", "reqId", "route", "sourcingChannels", "state", "targetClosure",
            ],
            req.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        var reqId = req.GetProperty("reqId").GetString()!;
        Assert.Matches(@"^REQ-\d{4}-\d{4}$", reqId);
        Assert.Equal("PendingApproval", req.GetProperty("state").GetString());
        Assert.Equal("A. Sharma", req.GetProperty("owner").GetString());
        Assert.Equal("hrhead", req.GetProperty("route").GetProperty("approverRole").GetString());
        Assert.Contains(api.Workflows.Started, s => s.Subject.Id == reqId);
        Assert.Equal(["recruitment.mrf.submitted.v1"], await OutboxTypesAsync(reqId));

        var jd = await HrTa().GetFromJsonAsync<JsonElement>($"{Requisitions}/{reqId}/job-description");
        Assert.Equal(1, jd.GetProperty("version").GetInt32());
        Assert.Equal("CA / MBA-Finance", jd.GetProperty("minQualification").GetString());
    }

    [Fact]
    public async Task Out_of_budget_routes_to_the_OOB_approver()
    {
        var req = await RaiseAsync(Input(outOfBudget: true));

        Assert.Equal("mdceo", req.GetProperty("route").GetProperty("approverRole").GetString());
    }

    [Fact]
    public async Task Sourcing_stays_locked_until_the_workflow_approves_then_unlocks_once()
    {
        var reqId = (await RaiseAsync()).GetProperty("reqId").GetString()!;
        var gate = await HrTa().GetFromJsonAsync<JsonElement>($"{Requisitions}/{reqId}/sourcing-gate");
        Assert.False(gate.GetProperty("allowed").GetBoolean());

        await CompleteWorkflowAsync(reqId, "Approved");

        gate = await api.ClientFor(RequisitionApiFactory.TenantA, RecuroRoles.Service, "svc-pipeline").GetFromJsonAsync<JsonElement>($"{Requisitions}/{reqId}/sourcing-gate");
        Assert.True(gate.GetProperty("allowed").GetBoolean());
        Assert.Equal("Approved", gate.GetProperty("state").GetString());
        Assert.Equal(
            ["recruitment.mrf.submitted.v1", "recruitment.mrf.approved.v1", "recruitment.sourcing.unlocked.v1"],
            await OutboxTypesAsync(reqId));
    }

    [Fact]
    public async Task A_rejection_records_the_reason()
    {
        var reqId = (await RaiseAsync()).GetProperty("reqId").GetString()!;

        await CompleteWorkflowAsync(reqId, "Rejected", "Budget frozen this quarter");

        var req = await HrTa().GetFromJsonAsync<JsonElement>($"{Requisitions}/{reqId}");
        Assert.Equal("Rejected", req.GetProperty("state").GetString());
        Assert.Contains("recruitment.mrf.rejected.v1", await OutboxTypesAsync(reqId));
    }

    [Fact]
    public async Task Drafts_autosave_with_optimistic_locking_then_submit()
    {
        var created = await HrTa().PostAsJsonAsync($"{Requisitions}?draft=true", new { designation = "Credit Analyst", positions = 0, employmentType = "Permanent", nature = "NewPosition" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = await created.Content.ReadFromJsonAsync<JsonElement>();
        var draftId = draft.GetProperty("reqId").GetString()!;
        Assert.StartsWith("DRAFT-", draftId, StringComparison.Ordinal);
        Assert.Equal("Draft", draft.GetProperty("state").GetString());
        var etag = created.Headers.ETag!;

        var stale = new HttpRequestMessage(HttpMethod.Patch, $"{Requisitions}/{draftId}") { Content = JsonContent.Create(Input("Credit Analyst")) };
        stale.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
        Assert.Equal(HttpStatusCode.Conflict, (await HrTa().SendAsync(stale)).StatusCode);

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"{Requisitions}/{draftId}") { Content = JsonContent.Create(Input("Credit Analyst")) };
        patch.Headers.IfMatch.Add(etag);
        var saved = await HrTa().SendAsync(patch);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.NotEqual(etag, saved.Headers.ETag);

        var submitted = await HrTa().PostAsync($"{Requisitions}/{draftId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var req = await submitted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PendingApproval", req.GetProperty("state").GetString());
        Assert.Matches(@"^REQ-\d{4}-\d{4}$", req.GetProperty("reqId").GetString()!);
    }

    [Fact]
    public async Task An_incomplete_MRF_returns_400_with_field_errors_and_saves_nothing()
    {
        var response = await HrTa().PostAsJsonAsync(Requisitions, new { designation = "X", positions = 1, employmentType = "Permanent", nature = "Replacement" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
        var fields = problem.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToList();
        Assert.Contains("department", fields);
        Assert.Contains("replacementReason", fields);
    }

    [Fact]
    public async Task An_unknown_grade_is_rejected_by_the_rules()
    {
        var response = await HrTa().PostAsJsonAsync(Requisitions, Input(grade: "ZZ"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("unknown_grade", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task When_Workflow_is_down_submit_returns_503_and_the_MRF_stays_a_draft()
    {
        api.Workflows.FailFor.Add("Branch Manager — Outage");

        var response = await HrTa().PostAsJsonAsync(Requisitions, Input("Branch Manager — Outage"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var drafts = await HrTa().GetFromJsonAsync<JsonElement>($"{Requisitions}?state=Draft&q=Outage");
        var draft = Assert.Single(drafts.EnumerateArray());
        Assert.Equal("Draft", draft.GetProperty("state").GetString());
    }

    [Theory]
    [InlineData(RecuroRoles.HrHead)]
    [InlineData(RecuroRoles.MdCeo)]
    [InlineData(RecuroRoles.Candidate)]
    public async Task Only_HR_TA_raises_MRFs(string role)
    {
        var response = await api.ClientFor(RequisitionApiFactory.TenantA, role).PostAsJsonAsync(Requisitions, Input());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Candidates_cannot_read_the_tracker_and_anonymous_callers_get_401()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(RequisitionApiFactory.TenantA, RecuroRoles.Candidate).GetAsync(Requisitions)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync(Requisitions)).StatusCode);
    }

    [Fact]
    public async Task Another_tenant_never_sees_the_requisition()
    {
        var reqId = (await RaiseAsync()).GetProperty("reqId").GetString()!;
        var other = HrTa(RequisitionApiFactory.TenantB);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Requisitions}/{reqId}")).StatusCode);
        var list = await other.GetFromJsonAsync<JsonElement>(Requisitions);
        Assert.DoesNotContain(list.EnumerateArray(), r => r.GetProperty("reqId").GetString() == reqId);
    }

    [Fact]
    public async Task Each_tenant_has_its_own_REQ_ID_sequence()
    {
        var first = (await RaiseAsync(tenant: RequisitionApiFactory.TenantB)).GetProperty("reqId").GetString()!;
        var second = (await RaiseAsync(tenant: RequisitionApiFactory.TenantB)).GetProperty("reqId").GetString()!;

        Assert.Equal(int.Parse(first[^4..], System.Globalization.CultureInfo.InvariantCulture) + 1, int.Parse(second[^4..], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task The_JD_builder_validates_and_freezes_a_new_version()
    {
        var reqId = (await RaiseAsync()).GetProperty("reqId").GetString()!;
        var jd = await HrTa().GetFromJsonAsync<JsonElement>($"{Requisitions}/{reqId}/job-description");
        var id = jd.GetProperty("id").GetString();

        var invalid = await HrTa().PostAsJsonAsync($"/api/v1/job-descriptions/{id}/submit", new { purpose = "", responsibilities = new[] { "one" }, competencies = Array.Empty<string>(), assessments = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var valid = await HrTa().PostAsJsonAsync($"/api/v1/job-descriptions/{id}/submit", new
        {
            purpose = "Own end-to-end credit appraisal.",
            responsibilities = new[] { "Appraise proposals", "Recommend sanctions" },
            reportsTo = "N. Sharma",
            teamSize = "None",
            location = "HQ — Mumbai",
            minQualification = "CA",
            experience = "6–9 years",
            grade = "M3",
            competencies = new[] { "domain", "analytical", "integrity" },
            assessments = new[] { "Case study" },
            benchmark = "≥ 65%",
        });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        var frozen = await valid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, frozen.GetProperty("version").GetInt32());
        Assert.Equal("Submitted", frozen.GetProperty("status").GetString());
        Assert.Equal(2, frozen.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task HR_Head_cancels_an_approved_requisition_with_a_reason()
    {
        var reqId = (await RaiseAsync()).GetProperty("reqId").GetString()!;
        await CompleteWorkflowAsync(reqId, "Approved");
        var head = api.ClientFor(RequisitionApiFactory.TenantA, RecuroRoles.HrHead);

        Assert.Equal(HttpStatusCode.BadRequest, (await head.PostAsJsonAsync($"{Requisitions}/{reqId}/cancel", new { reason = "no" })).StatusCode);
        var response = await head.PostAsJsonAsync($"{Requisitions}/{reqId}/cancel", new { reason = "Role merged into another requisition" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("recruitment.mrf.cancelled.v1", await OutboxTypesAsync(reqId));
    }

    [Fact]
    public async Task Events_carry_the_tenant_and_actor()
    {
        var reqId = (await RaiseAsync()).GetProperty("reqId").GetString()!;
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(RequisitionApiFactory.TenantA);
        var db = scope.ServiceProvider.GetRequiredService<RequisitionDbContext>();

        var row = (await db.OutboxMessages.AsNoTracking().ToListAsync()).Single(m => m.Envelope.Contains(reqId, StringComparison.Ordinal));
        using var envelope = JsonDocument.Parse(row.Envelope);
        Assert.Equal(RequisitionApiFactory.TenantA.ToString(), envelope.RootElement.GetProperty("tenantid").GetString());
        Assert.Equal("A. Sharma", envelope.RootElement.GetProperty("actorname").GetString());
        Assert.Equal("in", envelope.RootElement.GetProperty("data").GetProperty("budgetStatus").GetString());
        Assert.Equal(reqId, envelope.RootElement.GetProperty("data").GetProperty("reqId").GetString());
    }

    [Fact]
    public async Task The_ta_dashboard_counts_open_mrfs_for_the_callers_tenant()
    {
        var tenant = Guid.NewGuid();
        var empty = await HrTa(tenant).GetFromJsonAsync<JsonElement>($"{Requisitions}/dashboard/ta");
        Assert.Equal("0", empty.GetProperty("stats")[0].GetProperty("value").GetString());

        await RaiseAsync(tenant: tenant);
        var draft = await HrTa(tenant).PostAsJsonAsync($"{Requisitions}?draft=true", Input());
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);

        var tile = (await HrTa(tenant).GetFromJsonAsync<JsonElement>($"{Requisitions}/dashboard/ta")).GetProperty("stats")[0];
        Assert.Equal("Open MRFs", tile.GetProperty("label").GetString());
        Assert.Equal("1", tile.GetProperty("value").GetString());
        Assert.Equal("▲ +1 this week", tile.GetProperty("trend").GetString());
        Assert.Equal(string.Empty, tile.GetProperty("tone").GetString());
    }
}
