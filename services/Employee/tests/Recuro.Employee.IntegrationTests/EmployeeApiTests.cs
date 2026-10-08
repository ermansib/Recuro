using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Employee.Infrastructure.Persistence;

namespace Recuro.Employee.IntegrationTests;

public sealed class EmployeeApiTests(EmployeeApiFactory api) : IClassFixture<EmployeeApiFactory>
{
    private static readonly Guid A = EmployeeApiFactory.TenantA;
    private static readonly Guid B = EmployeeApiFactory.TenantB;

    private static string NewReqId() => $"REQ-E-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    private static string Email(string who = "ravi") => $"{who}.{Guid.NewGuid():N}@aurora.example";

    private static object Opening(string title = "Credit Manager", int minTenureMonths = 12) => new
    {
        title,
        location = "Jaipur",
        department = "Credit",
        grade = "M1",
        eligibleGrades = new[] { "E3", "E4" },
        minTenureMonths,
        summary = "Lead credit appraisal for the Jaipur cluster.",
    };

    private HttpClient Hr(Guid tenant) => api.ClientFor(tenant, RecuroRoles.HrTa, "hr-1", "P. Mehta");

    private HttpClient Staff(Guid tenant, string user = "emp-1", string name = "Ravi Kumar") => api.ClientFor(tenant, RecuroRoles.Employee, user, name);

    /// <summary>Unlocks sourcing at <paramref name="unlockedAt"/> and posts the opening internally.</summary>
    private async Task<string> OpeningAsync(Guid tenant, DateTimeOffset? unlockedAt = null, object? opening = null)
    {
        var reqId = NewReqId();
        await ProcessAsync(tenant, EventTypes.Requisition.SourcingUnlocked, new { reqId }, unlockedAt ?? DateTimeOffset.UtcNow.AddHours(-1));
        var posted = await Hr(tenant).PutAsJsonAsync($"/api/v1/employee/ijp/{reqId}", opening ?? Opening());
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        return reqId;
    }

    private static object Application(string? email = null, string grade = "E4", string joinedOn = "2022-04-01", bool consent = true) => new
    {
        email = email ?? Email(),
        phone = "+91 98290 44120",
        currentGrade = grade,
        joinedOn,
        experienceYears = 6,
        privacyConsent = consent,
    };

    private static object Referral(string reqId, string? email = null, string relationship = "FormerColleague", bool coi = true) => new
    {
        reqId,
        name = "Neha Joshi",
        email = email ?? Email("neha"),
        phone = "+91 99280 11223",
        experienceYears = 4,
        relationship,
        coiAccepted = coi,
    };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> FirstFieldCodeAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("errors")[0].GetProperty("code").GetString();

    [Fact]
    public async Task Hr_ta_posts_an_opening_only_after_sourcing_unlocks_and_employees_of_that_tenant_see_it()
    {
        var locked = NewReqId();
        var refused = await Hr(A).PutAsJsonAsync($"/api/v1/employee/ijp/{locked}", Opening());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("sourcing_locked", (await JsonAsync(refused)).GetProperty("code").GetString());

        var reqId = await OpeningAsync(A);

        var listed = await Staff(A).GetFromJsonAsync<JsonElement>("/api/v1/employee/ijp");
        var opening = listed.EnumerateArray().Single(p => p.GetProperty("reqId").GetString() == reqId);
        Assert.Equal("Credit Manager", opening.GetProperty("title").GetString());
        Assert.True(opening.GetProperty("closesAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddDays(4));

        var otherTenant = await Staff(B).GetFromJsonAsync<JsonElement>("/api/v1/employee/ijp");
        Assert.DoesNotContain(otherTenant.EnumerateArray(), p => p.GetProperty("reqId").GetString() == reqId);
    }

    [Fact]
    public async Task Employees_cannot_post_openings_and_hr_cannot_apply_internally()
    {
        var reqId = await OpeningAsync(A);

        Assert.Equal(HttpStatusCode.Forbidden, (await Staff(A).PutAsJsonAsync($"/api/v1/employee/ijp/{reqId}", Opening())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Hr(A).PostAsJsonAsync($"/api/v1/employee/ijp/{reqId}/applications", Application())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Hr(A).PostAsJsonAsync("/api/v1/employee/referrals", Referral(reqId))).StatusCode);
    }

    [Fact]
    public async Task An_opening_past_its_window_is_not_listed_and_refuses_applications()
    {
        var reqId = await OpeningAsync(A, DateTimeOffset.UtcNow.AddDays(-30));

        var listed = await Staff(A).GetFromJsonAsync<JsonElement>("/api/v1/employee/ijp");
        Assert.DoesNotContain(listed.EnumerateArray(), p => p.GetProperty("reqId").GetString() == reqId);

        var refused = await Staff(A).PostAsJsonAsync($"/api/v1/employee/ijp/{reqId}/applications", Application());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("ijp_window_closed", (await JsonAsync(refused)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_eligible_employee_applies_once_as_an_ijp_candidate_signed_as_the_service()
    {
        var reqId = await OpeningAsync(A);
        var email = Email();

        var applied = await Staff(A, "emp-applies").PostAsJsonAsync($"/api/v1/employee/ijp/{reqId}/applications", Application(email));

        Assert.Equal(HttpStatusCode.Created, applied.StatusCode);
        var body = await JsonAsync(applied);
        var appId = body.GetProperty("appId").GetString()!;
        Assert.StartsWith("APP-", appId, StringComparison.Ordinal);
        Assert.Equal("Submitted", body.GetProperty("status").GetString());

        var candidate = api.Downstream.Bodies.Single(b => b.Path == "/api/v1/candidates" && b.Body.GetProperty("email").GetString() == email).Body;
        Assert.Equal("IJP", candidate.GetProperty("source").GetString());
        Assert.Equal("employee-portal", candidate.GetProperty("consentSource").GetString());
        Assert.Contains(api.Downstream.Calls, c => c.Path == "/api/v1/candidates" && c.Roles == RecuroRoles.Service && c.Tenant == A.ToString());
        Assert.Equal(1, await OutboxCountAsync(A, EventTypes.Employee.IjpApplied, appId));

        var again = await Staff(A, "emp-applies").PostAsJsonAsync($"/api/v1/employee/ijp/{reqId}/applications", Application(email));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already_applied", (await JsonAsync(again)).GetProperty("code").GetString());

        var mine = await Staff(A, "emp-applies").GetFromJsonAsync<JsonElement>("/api/v1/employee/me/applications");
        Assert.Equal(appId, Assert.Single(mine.EnumerateArray()).GetProperty("appId").GetString());
        var someoneElse = await Staff(A, "emp-other").GetFromJsonAsync<JsonElement>("/api/v1/employee/me/applications");
        Assert.Empty(someoneElse.EnumerateArray());
    }

    [Fact]
    public async Task Grade_band_tenure_and_consent_are_checked_with_field_errors()
    {
        var reqId = await OpeningAsync(A);
        var url = $"/api/v1/employee/ijp/{reqId}/applications";

        var wrongGrade = await Staff(A).PostAsJsonAsync(url, Application(grade: "M3"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongGrade.StatusCode);
        Assert.Equal("grade_not_eligible", await FirstFieldCodeAsync(wrongGrade));

        var tooNew = await Staff(A).PostAsJsonAsync(url, Application(joinedOn: DateTime.UtcNow.AddMonths(-3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(HttpStatusCode.BadRequest, tooNew.StatusCode);
        Assert.Equal("tenure_too_short", await FirstFieldCodeAsync(tooNew));

        var noConsent = await Staff(A).PostAsJsonAsync(url, Application(consent: false));
        Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);
        Assert.Equal("consent_required", await FirstFieldCodeAsync(noConsent));
    }

    [Fact]
    public async Task A_referral_needs_the_coi_declaration_and_becomes_a_referral_sourced_application()
    {
        var reqId = await OpeningAsync(A);

        var noCoi = await Staff(A, "emp-refers").PostAsJsonAsync("/api/v1/employee/referrals", Referral(reqId, coi: false));
        Assert.Equal(HttpStatusCode.BadRequest, noCoi.StatusCode);
        Assert.Equal("consent_required", await FirstFieldCodeAsync(noCoi));

        var email = Email("neha");
        var referred = await Staff(A, "emp-refers").PostAsJsonAsync("/api/v1/employee/referrals", Referral(reqId, email));

        Assert.Equal(HttpStatusCode.Created, referred.StatusCode);
        var body = await JsonAsync(referred);
        Assert.True(body.GetProperty("bonusEligible").GetBoolean());
        var appId = body.GetProperty("appId").GetString()!;
        var candidate = api.Downstream.Bodies.Single(b => b.Path == "/api/v1/candidates" && b.Body.GetProperty("email").GetString() == email).Body;
        Assert.Equal("Referral", candidate.GetProperty("source").GetString());
        Assert.Equal("emp-refers", candidate.GetProperty("referrerId").GetString());
        Assert.Equal(1, await OutboxCountAsync(A, EventTypes.Employee.ReferralSubmitted, appId));

        var mine = await Staff(A, "emp-refers").GetFromJsonAsync<JsonElement>("/api/v1/employee/me/referrals");
        Assert.Equal(appId, Assert.Single(mine.EnumerateArray()).GetProperty("appId").GetString());
    }

    [Fact]
    public async Task Relatives_and_people_already_on_file_earn_no_referral_bonus()
    {
        var reqId = await OpeningAsync(A);

        var relative = await JsonAsync(await Staff(A).PostAsJsonAsync("/api/v1/employee/referrals", Referral(reqId, relationship: "relative")));
        Assert.False(relative.GetProperty("bonusEligible").GetBoolean());

        var known = Email("known");
        api.Downstream.Existing(known);
        var existing = await JsonAsync(await Staff(A).PostAsJsonAsync("/api/v1/employee/referrals", Referral(reqId, known)));
        Assert.False(existing.GetProperty("bonusEligible").GetBoolean());
        Assert.Equal("Submitted", existing.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_refused_application_step_tombstones_the_candidate_and_publishes_nothing()
    {
        var reqId = await OpeningAsync(A);
        api.Downstream.PipelineRefuses = "sourcing_locked";
        try
        {
            var email = Email("refused");
            var refused = await Staff(A, "emp-refused").PostAsJsonAsync("/api/v1/employee/referrals", Referral(reqId, email));

            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Contains(api.Downstream.Existing(email), api.Downstream.Tombstoned);
            var mine = await Staff(A, "emp-refused").GetFromJsonAsync<JsonElement>("/api/v1/employee/me/referrals");
            Assert.Equal("NotSubmitted", Assert.Single(mine.EnumerateArray()).GetProperty("status").GetString());
        }
        finally
        {
            api.Downstream.PipelineRefuses = null;
        }
    }

    [Fact]
    public async Task Pipeline_progress_is_mirrored_coarsely_and_a_cancelled_requisition_withdraws_the_opening()
    {
        var reqId = await OpeningAsync(A);
        var applied = await JsonAsync(await Staff(A, "emp-progress").PostAsJsonAsync($"/api/v1/employee/ijp/{reqId}/applications", Application()));
        var appId = applied.GetProperty("appId").GetString()!;

        await ProcessAsync(A, EventTypes.Pipeline.StageChanged, new { appId, from = "Screened", to = "Interview" }, DateTimeOffset.UtcNow);
        var mine = await Staff(A, "emp-progress").GetFromJsonAsync<JsonElement>("/api/v1/employee/me/applications");
        Assert.Equal("Interview", Assert.Single(mine.EnumerateArray()).GetProperty("progress").GetString());

        await ProcessAsync(A, EventTypes.Requisition.Cancelled, new { reqId, reason = "Budget withdrawn" }, DateTimeOffset.UtcNow);
        var listed = await Staff(A).GetFromJsonAsync<JsonElement>("/api/v1/employee/ijp");
        Assert.DoesNotContain(listed.EnumerateArray(), p => p.GetProperty("reqId").GetString() == reqId);
        var closed = await Staff(A, "emp-late").PostAsJsonAsync($"/api/v1/employee/ijp/{reqId}/applications", Application());
        Assert.Equal("ijp_withdrawn", (await JsonAsync(closed)).GetProperty("code").GetString());
    }

    private async Task<int> OutboxCountAsync(Guid tenant, string type, string contains)
    {
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenant);
        var db = scope.ServiceProvider.GetRequiredService<EmployeeDbContext>();
        var envelopes = await db.OutboxMessages.Where(m => m.TenantId == tenant && m.Type == type).Select(m => m.Envelope).ToListAsync();
        return envelopes.Count(e => e.Contains($"\"{contains}\"", StringComparison.Ordinal));
    }

    private Task ProcessAsync(Guid tenant, string type, object data, DateTimeOffset time) =>
        api.Services.GetRequiredService<IntegrationEventProcessor>().ProcessAsync(
            new CloudEvent
            {
                Id = Guid.CreateVersion7(),
                Source = "/services/test",
                Type = type,
                Subject = "test",
                Time = time,
                TenantId = tenant,
                ActorId = "u-7",
                ActorName = "R. Iyer",
                ActorRole = RecuroRoles.HrHead,
                Data = JsonSerializer.SerializeToElement(data, EventJson.Options),
            },
            CancellationToken.None);
}
