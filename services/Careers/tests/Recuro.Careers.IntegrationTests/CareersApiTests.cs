using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Careers.Domain.Applications;
using Recuro.Careers.Infrastructure.Persistence;

namespace Recuro.Careers.IntegrationTests;

public sealed class CareersApiTests(CareersApiFactory api) : IClassFixture<CareersApiFactory>
{
    private static readonly Guid A = CareersApiFactory.TenantA;
    private static readonly Guid B = CareersApiFactory.TenantB;

    private static string NewReqId() => $"REQ-T-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    private static object Content(string title = "Credit Analyst — Affordable Housing", string location = "Jaipur") => new
    {
        title,
        location = $"Branch {location}",
        locationFilter = location,
        experience = "2–4 yrs",
        qualification = "MBA-Finance",
        industry = "Housing Finance",
        tags = new[] { new { text = "Full-time", tone = "navy" } },
    };

    private Task<HttpResponseMessage> PutPostingAsync(Guid tenant, string reqId, object? content = null, string role = RecuroRoles.HrTa) =>
        api.ClientFor(tenant, role).PutAsJsonAsync($"/api/v1/careers/postings/{reqId}", content ?? Content());

    private Task<HttpResponseMessage> PublishAsync(Guid tenant, string reqId, object? body = null, string role = RecuroRoles.HrTa) =>
        api.ClientFor(tenant, role).PostAsJsonAsync($"/api/v1/careers/postings/{reqId}/publish", body ?? new { });

    /// <summary>A requisition unlocked long ago (IJP window over) with a published posting. Returns the posting id.</summary>
    private async Task<string> LivePostingAsync(Guid tenant, string? title = null, string location = "Jaipur")
    {
        var reqId = NewReqId();
        await ProcessAsync(tenant, EventTypes.Requisition.SourcingUnlocked, new { reqId }, DateTimeOffset.UtcNow.AddDays(-30));
        Assert.Equal(HttpStatusCode.OK, (await PutPostingAsync(tenant, reqId, Content(title ?? $"Analyst {reqId}", location))).StatusCode);
        var published = await PublishAsync(tenant, reqId);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        return (await published.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("posting").GetProperty("id").GetString()!;
    }

    private static object Application(string postingId, string email, bool privacy = true, bool coi = true) => new
    {
        postingId,
        name = "Aisha Khan",
        email,
        phone = "+91 98200 11223",
        experienceYears = 3,
        currentCtc = 8.5,
        expectedCtc = 11,
        noticeDays = 30,
        resumeFileName = "aisha-khan.pdf",
        privacyConsent = privacy,
        coiDeclaration = coi,
    };

    private Task<HttpResponseMessage> ApplyAsync(Guid tenant, object body, string? idempotencyKey = null)
    {
        var client = api.CreateClient();
        if (idempotencyKey is not null)
        {
            client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        }

        return client.PostAsJsonAsync($"/api/v1/careers/public/{tenant}/applications", body);
    }

    private static string Email() => $"aisha.{Guid.NewGuid():N}@email.example";

    [Fact]
    public async Task The_public_search_returns_live_postings_in_the_frontend_shape_and_only_the_tenants_own()
    {
        var postingId = await LivePostingAsync(A, location: "Kolhapur");
        await LivePostingAsync(B, location: "Kolhapur");

        var response = await api.CreateClient().GetAsync($"/api/v1/careers/public/{A}/jobs?location=kolhapur&limit=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=60", response.Headers.CacheControl?.ToString());
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(page.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetString() == postingId);
        Assert.Equal(
            ["experience", "id", "location", "locationFilter", "qualification", "reqId", "tags", "title"],
            item.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(page.GetProperty("items").EnumerateArray(), i => Assert.Equal("Kolhapur", i.GetProperty("locationFilter").GetString()));
    }

    [Fact]
    public async Task Search_pages_with_a_cursor_and_filters_by_text_and_industry()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 3; i++)
        {
            await LivePostingAsync(A, title: $"Branch Sales {marker} {i}");
        }

        var client = api.CreateClient();
        var first = await client.GetFromJsonAsync<JsonElement>($"/api/v1/careers/public/{A}/jobs?q={marker}&industry=housing%20finance&limit=2");
        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        var cursor = first.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);

        var second = await client.GetFromJsonAsync<JsonElement>($"/api/v1/careers/public/{A}/jobs?q={marker}&limit=2&cursor={cursor}");
        Assert.Single(second.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/careers/public/{A}/jobs?cursor=garbage!")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/careers/public/not-a-tenant/jobs")).StatusCode);
    }

    [Fact]
    public async Task Publishing_waits_for_sourcing_and_the_IJP_window_unless_the_HR_Head_opens_it_early()
    {
        var reqId = NewReqId();
        await PutPostingAsync(A, reqId);
        Assert.Equal(HttpStatusCode.Conflict, (await PublishAsync(A, reqId)).StatusCode);

        await ProcessAsync(A, EventTypes.Requisition.SourcingUnlocked, new { reqId }, DateTimeOffset.UtcNow);
        var published = await (await PublishAsync(A, reqId)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Published", published.GetProperty("status").GetString());
        Assert.True(published.GetProperty("visibleFrom").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddDays(4));
        var postingId = published.GetProperty("posting").GetProperty("id").GetString()!;
        Assert.DoesNotContain(postingId, await PublicIdsAsync(A));

        await api.ClientFor(A, RecuroRoles.HrTa).PostAsJsonAsync($"/api/v1/careers/postings/{reqId}/unpublish", new { reason = "Re-open early" });
        Assert.Equal(HttpStatusCode.Forbidden, (await PublishAsync(A, reqId, new { openBeforeIjpWindowEnds = true, justification = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PublishAsync(A, reqId, new { openBeforeIjpWindowEnds = true }, RecuroRoles.HrHead)).StatusCode);

        var early = await PublishAsync(A, reqId, new { openBeforeIjpWindowEnds = true, justification = "Niche skill, no internal pool" }, RecuroRoles.HrHead);
        Assert.Equal(HttpStatusCode.OK, early.StatusCode);
        var log = (await early.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("history").EnumerateArray().Last();
        Assert.Equal("PublishedEarly", log.GetProperty("action").GetString());
        Assert.Equal("Niche skill, no internal pool", log.GetProperty("reason").GetString());
        Assert.Contains(postingId, await PublicIdsAsync(A));
    }

    [Fact]
    public async Task A_cancelled_requisition_takes_its_posting_off_the_site()
    {
        var postingId = await LivePostingAsync(A);
        var reqId = await ReqIdOfAsync(A, postingId);

        await ProcessAsync(A, EventTypes.Requisition.Cancelled, new { reqId, reason = "Budget" }, DateTimeOffset.UtcNow);

        Assert.DoesNotContain(postingId, await PublicIdsAsync(A));
        Assert.Equal(HttpStatusCode.Conflict, (await PublishAsync(A, reqId)).StatusCode);
    }

    [Fact]
    public async Task Applying_without_both_consents_is_refused_with_field_errors()
    {
        var postingId = await LivePostingAsync(A);
        var candidateCalls = api.Downstream.Calls.Count(c => c.Path == "/api/v1/candidates");

        var response = await ApplyAsync(A, Application(postingId, Email(), privacy: false, coi: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString()).ToList();
        Assert.Contains("privacyConsent", fields, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("coiDeclaration", fields, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(candidateCalls, api.Downstream.Calls.Count(c => c.Path == "/api/v1/candidates"));
    }

    [Fact]
    public async Task A_valid_application_runs_the_intake_saga_as_the_service_and_issues_an_APP_ID()
    {
        var postingId = await LivePostingAsync(A);
        var email = Email();

        var response = await ApplyAsync(A, Application(postingId, email));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["appId", "position"], body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        var appId = body.GetProperty("appId").GetString()!;
        Assert.Matches(@"^APP-\d{4}-\d{4}$", appId);

        var intakeCalls = api.Downstream.Calls.Where(c => c.Path is "/api/v1/candidates" or "/api/v1/pipeline/applications").ToList();
        Assert.All(intakeCalls, c => Assert.Equal((RecuroRoles.Service, A.ToString()), (c.Roles, c.Tenant)));

        var application = await IntakeAsync(A, appId);
        Assert.Equal(IntakeState.Completed, application.State);
        Assert.Equal(["ConflictOfInterest", "DataPrivacy"], application.Consents.Select(c => c.Type).Order(StringComparer.Ordinal));
        Assert.All(application.Consents, c => Assert.Equal("v1", c.TextVersion));
        Assert.Equal(1, await OutboxCountAsync(A, EventTypes.Career.JobApplied, appId));
    }

    [Fact]
    public async Task A_known_person_is_reused_and_the_public_answer_does_not_reveal_it()
    {
        var postingId = await LivePostingAsync(A);
        var email = Email();
        var existing = api.Downstream.Existing(email);

        var response = await ApplyAsync(A, Application(postingId, email));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("duplicateOf", out _));
        var application = await IntakeAsync(A, body.GetProperty("appId").GetString()!);
        Assert.Equal(existing, application.DuplicateOf);
        Assert.False(application.CandidateCreatedHere);
    }

    [Fact]
    public async Task A_double_submit_with_the_same_key_returns_the_first_result_once()
    {
        var postingId = await LivePostingAsync(A);
        var email = Email();
        var key = Guid.NewGuid().ToString();
        var before = api.Downstream.PipelineCreates;

        var first = await (await ApplyAsync(A, Application(postingId, email), key)).Content.ReadFromJsonAsync<JsonElement>();
        var second = await ApplyAsync(A, Application(postingId, email), key);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(first.GetProperty("appId").GetString(), (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("appId").GetString());
        Assert.Equal(before + 1, api.Downstream.PipelineCreates);

        // Another applicant reusing the key gets their own application, never the first one's.
        var other = await (await ApplyAsync(A, Application(postingId, Email()), key)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(first.GetProperty("appId").GetString(), other.GetProperty("appId").GetString());
    }

    [Fact]
    public async Task When_Pipeline_refuses_the_saga_tombstones_the_candidate_it_created()
    {
        var postingId = await LivePostingAsync(A);
        var email = Email();
        api.Downstream.PipelineRefuses = "sourcing_locked";
        try
        {
            var response = await ApplyAsync(A, Application(postingId, email));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("position_closed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            Assert.Contains(api.Downstream.Existing(email), api.Downstream.Tombstoned);
            Assert.Equal(0, await OutboxCountAsync(A, EventTypes.Career.JobApplied, email));
        }
        finally
        {
            api.Downstream.PipelineRefuses = null;
        }
    }

    [Fact]
    public async Task An_existing_candidate_is_never_tombstoned()
    {
        var postingId = await LivePostingAsync(A);
        var email = Email();
        var existing = api.Downstream.Existing(email);
        api.Downstream.PipelineRefuses = "already_applied";
        try
        {
            var response = await ApplyAsync(A, Application(postingId, email));

            Assert.Equal("already_applied", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            Assert.DoesNotContain(existing, api.Downstream.Tombstoned);
        }
        finally
        {
            api.Downstream.PipelineRefuses = null;
        }
    }

    [Fact]
    public async Task Status_lookup_is_coarse_follows_Pipeline_and_never_enumerates()
    {
        var postingId = await LivePostingAsync(A);
        var appId = (await (await ApplyAsync(A, Application(postingId, Email()))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("appId").GetString()!;
        var client = api.CreateClient();

        var received = await client.GetFromJsonAsync<JsonElement>($"/api/v1/careers/public/{A}/applications/{appId.ToLowerInvariant()}/status");
        Assert.Equal("Received", received.GetProperty("stage").GetString());
        Assert.Equal("Under HR review — screening call within ≤7 days", received.GetProperty("status").GetString());

        await ProcessAsync(A, EventTypes.Pipeline.StageChanged, new { appId, from = "Screened", to = "Interview" }, DateTimeOffset.UtcNow);
        var interview = await client.GetFromJsonAsync<JsonElement>($"/api/v1/careers/public/{A}/applications/{appId}/status");
        Assert.Equal("Interview", interview.GetProperty("stage").GetString());

        var unknown = await client.GetAsync($"/api/v1/careers/public/{A}/applications/APP-1999-0001/status");
        var malformed = await client.GetAsync($"/api/v1/careers/public/{A}/applications/drop-table/status");
        var otherTenant = await client.GetAsync($"/api/v1/careers/public/{B}/applications/{appId}/status");
        Assert.All([unknown, malformed, otherTenant], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal(await unknown.Content.ReadAsStringAsync().ContinueWith(t => Title(t.Result)), Title(await otherTenant.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task A_final_rejection_shows_as_a_decision_and_the_regret_email_is_logged()
    {
        var postingId = await LivePostingAsync(A);
        var appId = (await (await ApplyAsync(A, Application(postingId, Email()))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("appId").GetString()!;
        var rejectedEvent = Guid.CreateVersion7();

        await ProcessAsync(A, EventTypes.Pipeline.FinalRejected, new { appId, reqId = "x", candidateId = "c", reason = "Not shortlisted", regretSendAt = "2026-10-12", retainUntil = "2027-10-08" }, DateTimeOffset.UtcNow, rejectedEvent);
        await ProcessAsync(A, EventTypes.Notification.EmailDispatched, new { emailId = Guid.NewGuid(), templateKey = "candidate.regret", sourceEventId = rejectedEvent, status = "Sent" }, DateTimeOffset.UtcNow);

        var status = await api.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/careers/public/{A}/applications/{appId}/status");
        Assert.Equal("Decision", status.GetProperty("stage").GetString());
        var application = await IntakeAsync(A, appId);
        Assert.Equal(new DateOnly(2026, 10, 12), application.RegretSendAt);
        Assert.NotNull(application.RegretDeliveredAt);
    }

    [Fact]
    public async Task Postings_are_HR_only_and_tenant_scoped()
    {
        var reqId = NewReqId();
        Assert.Equal(HttpStatusCode.Forbidden, (await PutPostingAsync(A, reqId, role: RecuroRoles.Employee)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutPostingAsync(A, reqId, role: RecuroRoles.MdCeo)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/v1/careers/postings")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutPostingAsync(A, reqId, new { title = "", tags = new[] { new { text = "x", tone = "pink" } } })).StatusCode);

        await PutPostingAsync(A, reqId);
        var mine = await api.ClientFor(A, RecuroRoles.MdCeo).GetFromJsonAsync<JsonElement>("/api/v1/careers/postings");
        var theirs = await api.ClientFor(B, RecuroRoles.HrTa).GetFromJsonAsync<JsonElement>("/api/v1/careers/postings");
        Assert.Contains(mine.EnumerateArray(), p => p.GetProperty("posting").GetProperty("reqId").GetString() == reqId);
        Assert.DoesNotContain(theirs.EnumerateArray(), p => p.GetProperty("posting").GetProperty("reqId").GetString() == reqId);
    }

    private static string? Title(string json) => JsonDocument.Parse(json).RootElement.GetProperty("title").GetString();

    private async Task<List<string?>> PublicIdsAsync(Guid tenant)
    {
        var page = await api.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/careers/public/{tenant}/jobs?limit=50");
        return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
    }

    private async Task<string> ReqIdOfAsync(Guid tenant, string postingId)
    {
        var postings = await api.ClientFor(tenant, RecuroRoles.HrTa).GetFromJsonAsync<JsonElement>("/api/v1/careers/postings");
        return postings.EnumerateArray().Select(p => p.GetProperty("posting")).First(p => p.GetProperty("id").GetString() == postingId).GetProperty("reqId").GetString()!;
    }

    private async Task<PublicApplication> IntakeAsync(Guid tenant, string appId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenant);
        var db = scope.ServiceProvider.GetRequiredService<CareersDbContext>();
        return await db.Applications.AsNoTracking().SingleAsync(a => a.AppId == appId);
    }

    private async Task<int> OutboxCountAsync(Guid tenant, string type, string contains)
    {
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenant);
        var db = scope.ServiceProvider.GetRequiredService<CareersDbContext>();
        var envelopes = await db.OutboxMessages.Where(m => m.TenantId == tenant && m.Type == type).Select(m => m.Envelope).ToListAsync();
        return envelopes.Count(e => e.Contains($"\"{contains}\"", StringComparison.Ordinal));
    }

    private Task ProcessAsync(Guid tenant, string type, object data, DateTimeOffset time, Guid? id = null) =>
        api.Services.GetRequiredService<IntegrationEventProcessor>().ProcessAsync(
            new CloudEvent
            {
                Id = id ?? Guid.CreateVersion7(),
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
