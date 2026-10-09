using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Onboarding.Infrastructure.Jobs;
using Recuro.Onboarding.Infrastructure.Persistence;

namespace Recuro.Onboarding.IntegrationTests;

public sealed class OnboardingApiTests(OnboardingApiFactory api) : IClassFixture<OnboardingApiFactory>
{
    private static readonly Guid A = OnboardingApiFactory.TenantA;

    private static readonly string[] MandatoryDocuments =
        ["identity-proof", "address-proof", "education-certificates", "photographs", "bank-details", "signed-declarations"];

    private static string NewAppId() => $"APP-T-{Guid.NewGuid():N}"[..24];

    private DateOnly Today => DateOnly.FromDateTime(api.Clock.GetUtcNow().UtcDateTime);

    /// <summary>Offer reports an acceptance, as <c>offer.accepted.v1</c> would.</summary>
    private async Task<string> AcceptedAsync(Guid tenant, DateOnly joiningDate, string? appId = null, string? offerId = null, int? probationMonths = null)
    {
        appId ??= NewAppId();
        await ProcessAsync(tenant, EventTypes.Offer.Accepted, new
        {
            offerId = offerId ?? Guid.NewGuid().ToString(),
            appId,
            reqId = "MRF-2026-0156",
            candidateId = Guid.NewGuid().ToString(),
            joiningDate = joiningDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            probationMonths,
        });
        return appId;
    }

    private Task<HttpResponseMessage> TickAsync(Guid tenant, string appId, string key, bool done = true, string role = RecuroRoles.HrTa) =>
        api.ClientFor(tenant, role).PutAsJsonAsync($"/api/v1/onboarding/cases/{appId}/checklist/{key}", new { done, remarks = $"{key} checked" });

    private async Task TickAllAsync(Guid tenant, string appId)
    {
        var onboardingCase = await GetCaseAsync(tenant, appId);
        foreach (var item in onboardingCase.GetProperty("checklist").EnumerateArray())
        {
            Assert.Equal(HttpStatusCode.OK, (await TickAsync(tenant, appId, item.GetProperty("key").GetString()!)).StatusCode);
        }
    }

    private Task<HttpResponseMessage> UploadAsync(Guid tenant, string appId, string type, byte[] content, string contentType = "application/pdf", string fileName = "scan.pdf")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return api.ClientFor(tenant, RecuroRoles.HrTa).PutAsync($"/api/v1/onboarding/cases/{appId}/documents/{type}", form);
    }

    private Task<HttpResponseMessage> ReviewAsync(Guid tenant, string appId, string type, bool verified, string? note = null) =>
        api.ClientFor(tenant, RecuroRoles.HrTa).PostAsJsonAsync($"/api/v1/onboarding/cases/{appId}/documents/{type}/review", new { verified, note });

    private async Task VerifyMandatoryAsync(Guid tenant, string appId)
    {
        foreach (var type in MandatoryDocuments)
        {
            Assert.Equal(HttpStatusCode.OK, (await UploadAsync(tenant, appId, type, Encoding.UTF8.GetBytes($"%PDF {type}"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(tenant, appId, type, true)).StatusCode);
        }
    }

    private Task<HttpResponseMessage> DecideAsync(Guid tenant, string appId, object body, string role = RecuroRoles.HrHead) =>
        api.ClientFor(tenant, role).PostAsJsonAsync($"/api/v1/onboarding/cases/{appId}/probation/decision", body);

    private Task<HttpResponseMessage> DecideAsHodAsync(Guid tenant, string appId, string userId, string name, object body) =>
        api.ClientFor(tenant, "employee,hod", userId, name).PostAsJsonAsync($"/api/v1/onboarding/cases/{appId}/probation/decision", body);

    [Fact]
    public async Task An_accepted_offer_opens_the_case_and_sends_the_joining_instructions_once()
    {
        var joining = Today.AddDays(40);
        var appId = await AcceptedAsync(A, joining);
        await ProcessAsync(A, EventTypes.Offer.Accepted, new { appId, joiningDate = joining.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });

        var onboardingCase = await GetCaseAsync(A, appId);

        Assert.Equal(
            ["acceptedAt", "appId", "bgvStatus", "buddy", "cancelledAt", "candidateId", "checklist", "checklistPercent", "confirmedAt", "day1ReadyAt", "daysToJoining",
             "decisions", "department", "documents", "fileComplete", "fileCompletedAt", "id", "joiningDate", "milestones", "missingDocuments", "offerId", "probationCycle",
             "probationEndsOn", "probationMonths", "reportingManager", "reportingManagerId", "reqId", "rulesVersionId", "status"],
            onboardingCase.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("PreBoarding", onboardingCase.GetProperty("status").GetString());
        Assert.Equal(40, onboardingCase.GetProperty("daysToJoining").GetInt32());
        Assert.Equal("NotStarted", onboardingCase.GetProperty("bgvStatus").GetString());
        Assert.Equal(11, onboardingCase.GetProperty("checklist").GetArrayLength());
        Assert.Equal(6, onboardingCase.GetProperty("missingDocuments").GetArrayLength());
        Assert.Equal("frd-annexure-e", onboardingCase.GetProperty("rulesVersionId").GetString());

        var milestones = onboardingCase.GetProperty("milestones").EnumerateArray()
            .ToDictionary(m => m.GetProperty("kind").GetString()!, m => (Status: m.GetProperty("status").GetString(), DueOn: DateOnly.Parse(m.GetProperty("dueOn").GetString()!, CultureInfo.InvariantCulture)));
        Assert.Equal("Done", milestones["joining-instructions"].Status);
        Assert.Equal(StubServices.Weekdays(joining, -5), milestones["it-provisioning"].DueOn);
        Assert.True(milestones["engagement-t21"].DueOn <= joining.AddDays(-21));
        Assert.True(milestones["engagement-t7"].DueOn <= joining.AddDays(-7));
        Assert.Equal(joining.AddMonths(6), milestones["probation-end"].DueOn);

        Assert.Equal(1, await OutboxCountAsync(A, EventTypes.Onboarding.JoiningInstructionsSent, appId));
        Assert.Equal(1, await CaseCountAsync(A, appId));
    }

    [Fact]
    public async Task Config_onboarding_matrix_and_working_days_are_used_and_pinned_when_present()
    {
        api.Stubs.OnboardingMatrixVersion = "cfg-onboarding-3";
        api.Stubs.BackwardWorkingDays = true;
        try
        {
            var appId = await AcceptedAsync(A, Today.AddDays(30));
            var onboardingCase = await GetCaseAsync(A, appId);

            Assert.Equal("cfg-onboarding-3", onboardingCase.GetProperty("rulesVersionId").GetString());
            Assert.Equal(["laptop", "badge", "induction"], onboardingCase.GetProperty("checklist").EnumerateArray().Select(i => i.GetProperty("key").GetString()));
            Assert.Equal(3, onboardingCase.GetProperty("probationMonths").GetInt32());
            Assert.Equal(9, onboardingCase.GetProperty("documents").GetArrayLength());
        }
        finally
        {
            api.Stubs.OnboardingMatrixVersion = null;
            api.Stubs.BackwardWorkingDays = false;
        }
    }

    [Fact]
    public async Task Without_Config_an_acceptance_is_retried_not_planned_from_a_guess()
    {
        api.Stubs.ConfigDown = true;
        try
        {
            var appId = NewAppId();
            await Assert.ThrowsAnyAsync<Exception>(() => AcceptedAsync(A, Today.AddDays(30), appId));
            Assert.Equal(0, await CaseCountAsync(A, appId));
        }
        finally
        {
            api.Stubs.ConfigDown = false;
        }
    }

    [Fact]
    public async Task Event_handlers_call_Config_as_the_onboarding_service_account()
    {
        api.Stubs.ConfigCallRoles.Clear();

        await AcceptedAsync(A, Today.AddDays(30));

        Assert.NotEmpty(api.Stubs.ConfigCallRoles);
        Assert.All(api.Stubs.ConfigCallRoles, role => Assert.Equal(RecuroRoles.Service, role));
    }

    [Fact]
    public async Task The_checklist_reaches_Day1_Ready_at_100_percent_once_and_then_locks()
    {
        var appId = await AcceptedAsync(A, Today.AddDays(20));

        var first = await (await TickAsync(A, appId, "offer-acceptance")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(9, first.GetProperty("checklistPercent").GetInt32());
        var item = first.GetProperty("checklist")[0];
        Assert.True(item.GetProperty("done").GetBoolean());
        Assert.Equal("A. Sharma", item.GetProperty("updatedBy").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await TickAsync(A, appId, "astrology")).StatusCode);

        await TickAllAsync(A, appId);

        var ready = await GetCaseAsync(A, appId);
        Assert.Equal("Day1Ready", ready.GetProperty("status").GetString());
        Assert.Equal(100, ready.GetProperty("checklistPercent").GetInt32());
        Assert.Equal(1, await OutboxCountAsync(A, EventTypes.Onboarding.Day1Ready, appId));

        var locked = await TickAsync(A, appId, "id-card", done: false);
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Contains("checklist_locked", await locked.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Documents_are_stored_encrypted_and_the_file_completes_only_when_every_mandatory_one_is_verified()
    {
        var appId = await AcceptedAsync(A, Today.AddDays(20));

        var wrongType = await UploadAsync(A, appId, "identity-proof", [1, 2, 3], "application/zip", "id.zip");
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(A, appId, "horoscope", [1])).StatusCode);

        var blocked = await api.ClientFor(A, RecuroRoles.HrTa).PostAsync($"/api/v1/onboarding/cases/{appId}/file-complete", null);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("file_incomplete", problem.GetProperty("code").GetString());

        var secret = Encoding.UTF8.GetBytes("%PDF-1.7 passport 1234");
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(A, appId, "identity-proof", secret)).StatusCode);
        var stored = Directory.EnumerateFiles(api.DocumentRoot, "*", SearchOption.AllDirectories).Select(File.ReadAllBytes).ToList();
        Assert.NotEmpty(stored);
        Assert.DoesNotContain(stored, bytes => Encoding.UTF8.GetString(bytes).Contains("passport 1234", StringComparison.Ordinal));
        var download = await api.ClientFor(A, RecuroRoles.HrTa).GetAsync($"/api/v1/onboarding/cases/{appId}/documents/identity-proof/file");
        Assert.Equal(secret, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.BadRequest, (await ReviewAsync(A, appId, "identity-proof", false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ReviewAsync(A, appId, "identity-proof", false, "Expired passport")).StatusCode);

        await VerifyMandatoryAsync(A, appId);

        var status = await api.ClientFor(A, RecuroRoles.HrHead).GetFromJsonAsync<JsonElement>($"/api/v1/onboarding/cases/{appId}/file-status");
        Assert.Equal(0, status.GetProperty("missing").GetArrayLength());
        Assert.False(status.GetProperty("complete").GetBoolean());
        var complete = await api.ClientFor(A, RecuroRoles.HrTa).PostAsync($"/api/v1/onboarding/cases/{appId}/file-complete", null);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.True((await complete.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("complete").GetBoolean());
    }

    [Fact]
    public async Task The_scheduler_raises_due_milestones_and_opens_the_IT_ticket_once()
    {
        // Joining in 3 days: both touchpoints and the T-5 working-day ticket are already due.
        var appId = await AcceptedAsync(A, Today.AddDays(3));
        var job = api.Services.GetRequiredService<MilestoneSchedulerJob>();

        Assert.True(await job.ScanAllTenantsAsync(CancellationToken.None));
        Assert.True(await job.ScanAllTenantsAsync(CancellationToken.None));

        var milestones = (await GetCaseAsync(A, appId)).GetProperty("milestones").EnumerateArray().ToDictionary(m => m.GetProperty("kind").GetString()!);
        Assert.Equal("Due", milestones["engagement-t21"].GetProperty("status").GetString());
        Assert.Equal("Due", milestones["engagement-t7"].GetProperty("status").GetString());
        Assert.Equal("Done", milestones["it-provisioning"].GetProperty("status").GetString());
        Assert.StartsWith("IT-", milestones["it-provisioning"].GetProperty("ticketRef").GetString(), StringComparison.Ordinal);
        Assert.Equal("Scheduled", milestones["probation-check-in"].GetProperty("status").GetString());
        Assert.Equal(2, await OutboxCountAsync(A, EventTypes.Onboarding.MilestoneDue, appId));

        var t21 = milestones["engagement-t21"].GetProperty("id").GetString();
        var done = await api.ClientFor(A, RecuroRoles.HrTa).PostAsJsonAsync($"/api/v1/onboarding/cases/{appId}/milestones/{t21}/complete", new { notes = "Drop-out risk: low" });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var completed = (await done.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("milestones").EnumerateArray().Single(m => m.GetProperty("id").GetString() == t21);
        Assert.Equal("Drop-out risk: low", completed.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task Confirmation_needs_a_complete_file_and_a_cleared_BGV_then_issues_the_letter()
    {
        // Joined seven months ago: probation (6 months) has ended.
        var appId = await AcceptedAsync(A, Today.AddMonths(-7));
        await ProcessAsync(A, EventTypes.Bgv.CaseInitiated, new { caseId = "bgv-1", appId });
        await TickAllAsync(A, appId);

        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(A, appId, new { decision = "confirm" }, RecuroRoles.HrTa)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await DecideAsync(A, appId, new { decision = "promote" })).StatusCode);

        var noFile = await DecideAsync(A, appId, new { decision = "confirm" });
        Assert.Equal(HttpStatusCode.Conflict, noFile.StatusCode);
        Assert.Contains("file_incomplete", await noFile.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await VerifyMandatoryAsync(A, appId);
        var pendingBgv = await DecideAsync(A, appId, new { decision = "confirm" });
        Assert.Equal(HttpStatusCode.Conflict, pendingBgv.StatusCode);
        Assert.Contains("bgv_not_cleared", await pendingBgv.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await ProcessAsync(A, EventTypes.Bgv.Cleared, new { caseId = "bgv-1", appId, clearedAt = api.Clock.GetUtcNow() });
        var confirmed = await DecideAsync(A, appId, new { decision = "confirm", reason = "Met every goal" });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var body = await confirmed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Confirmed", body.GetProperty("status").GetString());
        Assert.Equal("Cleared", body.GetProperty("bgvStatus").GetString());
        Assert.Equal("Confirm", body.GetProperty("decisions")[0].GetProperty("outcome").GetString());
        Assert.Equal(1, await OutboxCountAsync(A, EventTypes.Onboarding.EmployeeConfirmed, appId));

        api.Stubs.CandidateCallRoles.Clear();
        var letter = await api.ClientFor(A, RecuroRoles.HrHead).GetAsync($"/api/v1/onboarding/cases/{appId}/confirmation-letter");
        Assert.Equal(HttpStatusCode.OK, letter.StatusCode);
        Assert.Equal("application/pdf", letter.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(await letter.Content.ReadAsByteArrayAsync())[..4], StringComparison.Ordinal);
        Assert.Equal([RecuroRoles.HrHead], api.Stubs.CandidateCallRoles);

        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(A, appId, new { decision = "extend", reason = "again", extendByMonths = 1 })).StatusCode);
    }

    [Fact]
    public async Task An_adverse_BGV_blocks_confirmation_but_an_extension_with_a_reason_starts_a_new_cycle()
    {
        var appId = await AcceptedAsync(A, Today.AddMonths(-7));
        await TickAllAsync(A, appId);
        await VerifyMandatoryAsync(A, appId);
        await ProcessAsync(A, EventTypes.Bgv.AdverseFlagged, new { caseId = "bgv-2", appId });

        var blocked = await DecideAsync(A, appId, new { decision = "confirm" });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("UnderReview", await blocked.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.BadRequest, (await DecideAsync(A, appId, new { decision = "extend", extendByMonths = 3 })).StatusCode);
        var extended = await DecideAsync(A, appId, new { decision = "extend", reason = "Awaiting BGV outcome", extendByMonths = 3 });
        Assert.Equal(HttpStatusCode.OK, extended.StatusCode);
        var body = await extended.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("probationCycle").GetInt32());
        Assert.Equal(2, body.GetProperty("milestones").EnumerateArray().Count(m => m.GetProperty("cycle").GetInt32() == 2));
        Assert.Equal(1, await OutboxCountAsync(A, EventTypes.Onboarding.ProbationExtended, appId));
    }

    [Fact]
    public async Task The_department_head_of_the_joiners_manager_decides_probation_and_gets_the_reminder()
    {
        var b = OnboardingApiFactory.TenantB;
        api.Stubs.AddPerson(A, "mgr-ops", "R. Iyer", "operations");
        api.Stubs.AddPerson(A, "hod-ops", "S. Menon", "operations", hod: true);
        api.Stubs.AddPerson(A, "hod-fin", "K. Das", "finance", hod: true);
        api.Stubs.AddPerson(b, "hod-ops-b", "B. Head", "operations", hod: true);
        var appId = await AcceptedAsync(A, Today.AddMonths(-7));
        await TickAllAsync(A, appId);
        await VerifyMandatoryAsync(A, appId);
        await ProcessAsync(A, EventTypes.Bgv.Cleared, new { caseId = "bgv-h1", appId, clearedAt = api.Clock.GetUtcNow() });
        var assigned = await api.ClientFor(A, RecuroRoles.HrTa).PutAsJsonAsync($"/api/v1/onboarding/cases/{appId}/assignments", new { reportingManagerId = "mgr-ops", reportingManager = "R. Iyer" });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        // Probation has ended: the review and end reminders name the operations head (found in tenant A only).
        Assert.True(await api.Services.GetRequiredService<MilestoneSchedulerJob>().ScanAllTenantsAsync(CancellationToken.None));
        var endReminder = await ReminderAsync(A, appId, "probation-end");
        Assert.Equal("hod-ops", endReminder.GetProperty("departmentHeadId").GetString());
        Assert.Equal(["hrta"], endReminder.GetProperty("assigneeRoles").EnumerateArray().Select(r => r.GetString()));

        var hrHead = await DecideAsync(A, appId, new { decision = "confirm" });
        Assert.Equal(HttpStatusCode.Forbidden, hrHead.StatusCode);
        Assert.Contains("not_department_head", await hrHead.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsHodAsync(A, appId, "hod-fin", "K. Das", new { decision = "confirm" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsHodAsync(A, appId, "hod-ops-b", "B. Head", new { decision = "confirm" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(A, RecuroRoles.Employee, "hod-ops").PostAsJsonAsync($"/api/v1/onboarding/cases/{appId}/probation/decision", new { decision = "confirm" })).StatusCode);

        var confirmed = await DecideAsHodAsync(A, appId, "hod-ops", "S. Menon", new { decision = "confirm", reason = "Strong first six months" });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var body = await confirmed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Confirmed", body.GetProperty("status").GetString());
        Assert.Equal("S. Menon", body.GetProperty("decisions")[0].GetProperty("decidedBy").GetString());
    }

    [Fact]
    public async Task Without_a_department_head_HR_Head_decides_probation()
    {
        api.Stubs.AddPerson(A, "hod-ops", "S. Menon", "operations", hod: true);
        var appId = await AcceptedAsync(A, Today.AddMonths(-7));
        await TickAllAsync(A, appId);
        var hrTa = api.ClientFor(A, RecuroRoles.HrTa);
        Assert.Equal(HttpStatusCode.BadRequest, (await hrTa.PutAsJsonAsync($"/api/v1/onboarding/cases/{appId}/assignments", new { department = "Legal Team" })).StatusCode);
        var assigned = await hrTa.PutAsJsonAsync($"/api/v1/onboarding/cases/{appId}/assignments", new { department = "legal" });
        Assert.Equal("legal", (await assigned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("department").GetString());

        Assert.True(await api.Services.GetRequiredService<MilestoneSchedulerJob>().ScanAllTenantsAsync(CancellationToken.None));
        var endReminder = await ReminderAsync(A, appId, "probation-end");
        Assert.Equal(JsonValueKind.Null, endReminder.GetProperty("departmentHeadId").ValueKind);
        Assert.Equal(["hrta", "hrhead"], endReminder.GetProperty("assigneeRoles").EnumerateArray().Select(r => r.GetString()));

        var hod = await DecideAsHodAsync(A, appId, "hod-ops", "S. Menon", new { decision = "extend", reason = "More time", extendByMonths = 2 });
        Assert.Equal(HttpStatusCode.Forbidden, hod.StatusCode);
        Assert.Contains("hr_head_decides", await hod.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        api.Stubs.IdentityDown = true;
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await DecideAsync(A, appId, new { decision = "extend", reason = "More time", extendByMonths = 2 })).StatusCode);
        }
        finally
        {
            api.Stubs.IdentityDown = false;
        }

        var extended = await DecideAsync(A, appId, new { decision = "extend", reason = "More time", extendByMonths = 2 });
        Assert.Equal(HttpStatusCode.OK, extended.StatusCode);
    }

    [Fact]
    public async Task A_withdrawn_offer_cancels_the_case_and_a_new_acceptance_opens_a_fresh_one()
    {
        var offerId = Guid.NewGuid().ToString();
        var appId = await AcceptedAsync(A, Today.AddDays(30), offerId: offerId);

        await ProcessAsync(A, EventTypes.Offer.Withdrawn, new { offerId = Guid.NewGuid().ToString(), appId, from = "Accepted", reason = "Other offer" });
        Assert.Equal("PreBoarding", (await GetCaseAsync(A, appId)).GetProperty("status").GetString());

        await ProcessAsync(A, EventTypes.Offer.Withdrawn, new { offerId, appId, from = "Accepted", reason = "Candidate relocated" });
        var cancelled = await GetCaseAsync(A, appId);
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.DoesNotContain(cancelled.GetProperty("milestones").EnumerateArray(), m => m.GetProperty("status").GetString() is "Scheduled" or "Due");
        Assert.Equal(HttpStatusCode.Conflict, (await TickAsync(A, appId, "id-card")).StatusCode);

        await AcceptedAsync(A, Today.AddDays(45), appId);
        Assert.Equal("PreBoarding", (await GetCaseAsync(A, appId)).GetProperty("status").GetString());
        Assert.Equal(2, await CaseCountAsync(A, appId));
    }

    [Fact]
    public async Task Roles_follow_the_RBAC_matrix()
    {
        var appId = await AcceptedAsync(A, Today.AddDays(30));

        Assert.Equal(HttpStatusCode.OK, (await api.ClientFor(A, RecuroRoles.HrHead).GetAsync($"/api/v1/onboarding/cases/{appId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.ClientFor(A, RecuroRoles.Service).GetAsync($"/api/v1/onboarding/cases/{appId}")).StatusCode);
        foreach (var role in new[] { RecuroRoles.MdCeo, RecuroRoles.Employee, RecuroRoles.Candidate })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(A, role).GetAsync($"/api/v1/onboarding/cases/{appId}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await TickAsync(A, appId, "id-card", role: RecuroRoles.Service)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await TickAsync(A, appId, "id-card", role: RecuroRoles.MdCeo)).StatusCode);

        var list = await api.ClientFor(A, RecuroRoles.HrTa).GetFromJsonAsync<JsonElement>("/api/v1/onboarding/cases");
        Assert.Contains(list.EnumerateArray(), c => c.GetProperty("appId").GetString() == appId);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.ClientFor(A, RecuroRoles.HrTa).GetAsync("/api/v1/onboarding/cases?status=Sleeping")).StatusCode);
    }

    [Fact]
    public async Task Tenant_B_cannot_read_or_change_tenant_A_cases()
    {
        var appId = await AcceptedAsync(A, Today.AddDays(30));
        var b = OnboardingApiFactory.TenantB;

        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor(b, RecuroRoles.HrTa).GetAsync($"/api/v1/onboarding/cases/{appId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await TickAsync(b, appId, "id-card")).StatusCode);
        var list = await api.ClientFor(b, RecuroRoles.HrTa).GetFromJsonAsync<JsonElement>("/api/v1/onboarding/cases");
        Assert.DoesNotContain(list.EnumerateArray(), c => c.GetProperty("appId").GetString() == appId);
    }

    private async Task<JsonElement> GetCaseAsync(Guid tenant, string caseRef) =>
        await api.ClientFor(tenant, RecuroRoles.HrTa).GetFromJsonAsync<JsonElement>($"/api/v1/onboarding/cases/{caseRef}");

    private async Task<int> CaseCountAsync(Guid tenant, string appId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenant);
        return await scope.ServiceProvider.GetRequiredService<OnboardingDbContext>().Cases.CountAsync(c => c.AppId == appId);
    }

    private async Task<int> OutboxCountAsync(Guid tenant, string type, string appId) =>
        (await OutboxEnvelopesAsync(tenant, type, appId)).Count;

    /// <summary>The <c>data</c> of the one milestone.due event for this case and milestone kind.</summary>
    private async Task<JsonElement> ReminderAsync(Guid tenant, string appId, string milestone) =>
        (await OutboxEnvelopesAsync(tenant, EventTypes.Onboarding.MilestoneDue, appId))
            .Select(e => JsonDocument.Parse(e).RootElement.GetProperty("data"))
            .Single(d => d.GetProperty("milestone").GetString() == milestone);

    private async Task<List<string>> OutboxEnvelopesAsync(Guid tenant, string type, string appId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenant);
        var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
        var envelopes = await db.OutboxMessages.Where(m => m.TenantId == tenant && m.Type == type).Select(m => m.Envelope).ToListAsync();
        return envelopes.Where(e => e.Contains($"\"{appId}\"", StringComparison.Ordinal)).ToList();
    }

    private Task ProcessAsync(Guid tenant, string type, object data) =>
        api.Services.GetRequiredService<IntegrationEventProcessor>().ProcessAsync(Event(tenant, type, data), CancellationToken.None);

    private CloudEvent Event(Guid tenant, string type, object data) => new()
    {
        Id = Guid.CreateVersion7(),
        Source = "/services/test",
        Type = type,
        Subject = "test",
        Time = api.Clock.GetUtcNow(),
        TenantId = tenant,
        ActorId = "u-9",
        ActorName = "V. Rao",
        ActorRole = RecuroRoles.HrTa,
        Data = JsonSerializer.SerializeToElement(data, EventJson.Options),
    };
}
