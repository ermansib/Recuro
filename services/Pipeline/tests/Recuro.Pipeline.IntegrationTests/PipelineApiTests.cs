using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Pipeline.Infrastructure.Jobs;
using Recuro.Pipeline.Infrastructure.Persistence;

namespace Recuro.Pipeline.IntegrationTests;

public sealed class PipelineApiTests(PipelineApiFactory api) : IClassFixture<PipelineApiFactory>
{
    private static string NewReqId() => $"MRF-T-{Guid.NewGuid():N}"[..20];

    private async Task<string> OpenRequisitionAsync(Guid tenant)
    {
        var reqId = NewReqId();
        await ProcessAsync(tenant, EventTypes.Requisition.SourcingUnlocked, new { reqId, targetClosure = "2026-12-31" });
        return reqId;
    }

    private async Task<HttpResponseMessage> PostApplicationAsync(Guid tenant, string reqId, string candidateId, string role = RecuroRoles.HrTa) =>
        await api.ClientFor(tenant, role).PostAsJsonAsync("/api/v1/pipeline/applications", new { reqId, candidateId, source = "Walk-in", note = "Walked in on Monday" });

    private async Task<JsonElement> CreateAsync(Guid tenant, string reqId, string? candidateId = null)
    {
        var response = await PostApplicationAsync(tenant, reqId, candidateId ?? api.Candidates.Add());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<HttpResponseMessage> MoveAsync(Guid tenant, JsonElement application, string to, string role = RecuroRoles.HrTa) =>
        api.ClientFor(tenant, role).PostAsJsonAsync($"/api/v1/pipeline/applications/{AppId(application)}/move", new { to });

    [Fact]
    public async Task HR_TA_creates_an_application_in_the_frontend_shape()
    {
        var reqId = await OpenRequisitionAsync(PipelineApiFactory.TenantA);

        var created = await CreateAsync(PipelineApiFactory.TenantA, reqId);

        Assert.Equal(
            ["appId", "candidateId", "note", "reqId", "stage", "stageHistory"],
            created.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Matches(@"^APP-\d{4}-\d{4}$", AppId(created));
        Assert.Equal("Sourced", created.GetProperty("stage").GetString());
        var first = Assert.Single(created.GetProperty("stageHistory").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("from").ValueKind);
        Assert.Equal("A. Sharma", first.GetProperty("by").GetString());
        Assert.Equal(1, await OutboxCountAsync(PipelineApiFactory.TenantA, EventTypes.Pipeline.ApplicationCreated, AppId(created)));
    }

    [Fact]
    public async Task Sourcing_is_locked_until_the_requisition_is_unlocked_and_again_after_cancellation()
    {
        var reqId = NewReqId();
        var locked = await PostApplicationAsync(PipelineApiFactory.TenantA, reqId, api.Candidates.Add());
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);

        await ProcessAsync(PipelineApiFactory.TenantA, EventTypes.Requisition.SourcingUnlocked, new { reqId });
        var application = await CreateAsync(PipelineApiFactory.TenantA, reqId);

        await ProcessAsync(PipelineApiFactory.TenantA, EventTypes.Requisition.Cancelled, new { reqId, reason = "Budget" });
        Assert.Equal("Withdrawn", (await GetAsync(PipelineApiFactory.TenantA, AppId(application))).GetProperty("stage").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await PostApplicationAsync(PipelineApiFactory.TenantA, reqId, api.Candidates.Add())).StatusCode);

        // A late unlock never reopens a cancelled requisition.
        await ProcessAsync(PipelineApiFactory.TenantA, EventTypes.Requisition.SourcingUnlocked, new { reqId });
        Assert.Equal(HttpStatusCode.Conflict, (await PostApplicationAsync(PipelineApiFactory.TenantA, reqId, api.Candidates.Add())).StatusCode);
    }

    [Fact]
    public async Task Unknown_candidates_and_duplicate_applications_are_refused()
    {
        var reqId = await OpenRequisitionAsync(PipelineApiFactory.TenantA);
        var candidateId = api.Candidates.Add();
        await CreateAsync(PipelineApiFactory.TenantA, reqId, candidateId);

        Assert.Equal(HttpStatusCode.Conflict, (await PostApplicationAsync(PipelineApiFactory.TenantA, reqId, candidateId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostApplicationAsync(PipelineApiFactory.TenantA, reqId, "nobody")).StatusCode);
    }

    [Fact]
    public async Task Moves_follow_the_state_machine()
    {
        var reqId = await OpenRequisitionAsync(PipelineApiFactory.TenantA);
        var application = await CreateAsync(PipelineApiFactory.TenantA, reqId);

        var illegal = await MoveAsync(PipelineApiFactory.TenantA, application, "Offer");
        Assert.Equal(HttpStatusCode.Conflict, illegal.StatusCode);

        var moved = await MoveAsync(PipelineApiFactory.TenantA, application, "Screened");
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var body = await moved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Screened", body.GetProperty("stage").GetString());
        Assert.Equal(2, body.GetProperty("stageHistory").GetArrayLength());
        Assert.Equal(1, await OutboxCountAsync(PipelineApiFactory.TenantA, EventTypes.Pipeline.StageChanged, AppId(application)));

        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(PipelineApiFactory.TenantA, application, "Nowhere")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(PipelineApiFactory.TenantA, application, "Rejected")).StatusCode);
    }

    [Fact]
    public async Task MD_CEO_reads_the_board_but_cannot_move_cards()
    {
        var reqId = await OpenRequisitionAsync(PipelineApiFactory.TenantA);
        var application = await CreateAsync(PipelineApiFactory.TenantA, reqId);

        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(PipelineApiFactory.TenantA, application, "Screened", RecuroRoles.MdCeo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostApplicationAsync(PipelineApiFactory.TenantA, reqId, api.Candidates.Add(), RecuroRoles.MdCeo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostApplicationAsync(PipelineApiFactory.TenantA, reqId, api.Candidates.Add(), RecuroRoles.Employee)).StatusCode);
        var board = await api.ClientFor(PipelineApiFactory.TenantA, RecuroRoles.MdCeo).GetAsync($"/api/v1/pipeline/requisitions/{reqId}/board");
        Assert.Equal(HttpStatusCode.OK, board.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"/api/v1/pipeline/requisitions/{reqId}/board")).StatusCode);
    }

    [Fact]
    public async Task Rejection_needs_a_reason_and_publishes_final_rejected()
    {
        var reqId = await OpenRequisitionAsync(PipelineApiFactory.TenantA);
        var application = await CreateAsync(PipelineApiFactory.TenantA, reqId);
        var client = api.ClientFor(PipelineApiFactory.TenantA, RecuroRoles.HrHead, "u-2", "R. Iyer");
        var url = $"/api/v1/pipeline/applications/{AppId(application)}/reject";

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, new { reason = " " })).StatusCode);
        var rejected = await client.PostAsJsonAsync(url, new { reason = "Skills gap" });

        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var body = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Rejected", body.GetProperty("stage").GetString());
        Assert.Equal("Skills gap", body.GetProperty("rejection").GetProperty("reason").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", body.GetProperty("rejection").GetProperty("regretDueBy").GetString()!);
        Assert.Equal(1, await OutboxCountAsync(PipelineApiFactory.TenantA, EventTypes.Pipeline.FinalRejected, AppId(application)));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(url, new { reason = "Again" })).StatusCode);
    }

    [Fact]
    public async Task The_board_groups_cards_by_column_with_candidates()
    {
        var reqId = await OpenRequisitionAsync(PipelineApiFactory.TenantA);
        var screened = await CreateAsync(PipelineApiFactory.TenantA, reqId);
        await CreateAsync(PipelineApiFactory.TenantA, reqId);
        var held = await CreateAsync(PipelineApiFactory.TenantA, reqId);
        await MoveAsync(PipelineApiFactory.TenantA, screened, "Screened");
        await MoveAsync(PipelineApiFactory.TenantA, held, "Hold");

        var board = await api.ClientFor(PipelineApiFactory.TenantA, RecuroRoles.HrTa)
            .GetFromJsonAsync<JsonElement>($"/api/v1/pipeline/requisitions/{reqId}/board");

        Assert.Equal(3, board.GetProperty("total").GetInt32());
        var columns = board.GetProperty("columns").EnumerateArray().ToList();
        Assert.Equal(["Sourced", "Screened", "Interview", "Selection", "BGV", "Offer"], columns.Select(c => c.GetProperty("stage").GetString()));
        Assert.Equal(1, columns[0].GetProperty("count").GetInt32());
        var card = Assert.Single(columns[1].GetProperty("cards").EnumerateArray());
        Assert.Equal(AppId(screened), card.GetProperty("application").GetProperty("appId").GetString());
        Assert.Equal("Rahul Mehta", card.GetProperty("candidate").GetProperty("name").GetString());
        Assert.Equal(1, board.GetProperty("offBoard").GetProperty("Hold").GetInt32());

        var cards = await api.ClientFor(PipelineApiFactory.TenantA, RecuroRoles.HrHead)
            .GetFromJsonAsync<JsonElement>($"/api/v1/pipeline/requisitions/{reqId}/cards");
        Assert.Equal(3, cards.GetArrayLength());
        Assert.Equal(["application", "candidate"], cards[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Tenants_never_see_each_others_applications()
    {
        var reqId = await OpenRequisitionAsync(PipelineApiFactory.TenantA);
        var application = await CreateAsync(PipelineApiFactory.TenantA, reqId);

        var other = api.ClientFor(PipelineApiFactory.TenantB, RecuroRoles.HrTa);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/pipeline/applications/{AppId(application)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await MoveAsync(PipelineApiFactory.TenantB, application, "Screened")).StatusCode);
        var cards = await other.GetFromJsonAsync<JsonElement>($"/api/v1/pipeline/requisitions/{reqId}/cards");
        Assert.Equal(0, cards.GetArrayLength());

        // The sourcing gate is per tenant too.
        Assert.Equal(HttpStatusCode.Conflict, (await PostApplicationAsync(PipelineApiFactory.TenantB, reqId, api.Candidates.Add())).StatusCode);
    }

    [Fact]
    public async Task Progress_events_advance_applications_once()
    {
        var reqId = await OpenRequisitionAsync(PipelineApiFactory.TenantA);
        var application = await CreateAsync(PipelineApiFactory.TenantA, reqId);
        foreach (var stage in new[] { "Screened", "Interview" })
        {
            await MoveAsync(PipelineApiFactory.TenantA, application, stage);
        }

        var ratified = Event(PipelineApiFactory.TenantA, EventTypes.Interview.SelectionRatified, new { appId = AppId(application) });
        var processor = api.Services.GetRequiredService<IntegrationEventProcessor>();
        await processor.ProcessAsync(ratified, CancellationToken.None);
        await processor.ProcessAsync(ratified, CancellationToken.None);

        var read = await GetAsync(PipelineApiFactory.TenantA, AppId(application));
        Assert.Equal("Selection", read.GetProperty("stage").GetString());
        Assert.Equal(4, read.GetProperty("stageHistory").GetArrayLength());

        // An event for another stage leaves the card where it is.
        await ProcessAsync(PipelineApiFactory.TenantA, EventTypes.Offer.Accepted, new { appId = AppId(application) });
        Assert.Equal("Selection", (await GetAsync(PipelineApiFactory.TenantA, AppId(application))).GetProperty("stage").GetString());
    }

    [Fact]
    public async Task The_TAT_scanner_flags_an_overrun_stage_once()
    {
        var tenant = Guid.NewGuid();
        var reqId = await OpenRequisitionAsync(tenant);
        var application = await CreateAsync(tenant, reqId);
        var job = api.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<TatScanJob>().Single();

        Assert.True(await job.ScanAllTenantsAsync(CancellationToken.None));
        Assert.Equal(0, await OutboxCountAsync(tenant, EventTypes.Pipeline.TatBreached, AppId(application)));

        api.Clock.Advance(TimeSpan.FromDays(12));
        try
        {
            await job.ScanAllTenantsAsync(CancellationToken.None);
            await job.ScanAllTenantsAsync(CancellationToken.None);
        }
        finally
        {
            api.Clock.Advance(TimeSpan.FromDays(-12));
        }

        Assert.Equal(1, await OutboxCountAsync(tenant, EventTypes.Pipeline.TatBreached, AppId(application)));
    }

    [Fact]
    public async Task The_regret_date_comes_from_the_Config_calendar_and_survives_an_outage()
    {
        var reqId = await OpenRequisitionAsync(PipelineApiFactory.TenantA);
        var today = DateOnly.FromDateTime(api.Clock.GetUtcNow().UtcDateTime);
        var url = (JsonElement a) => $"/api/v1/pipeline/applications/{AppId(a)}/reject";
        var client = api.ClientFor(PipelineApiFactory.TenantA, RecuroRoles.HrTa);

        var viaConfig = await (await client.PostAsJsonAsync(url(await CreateAsync(PipelineApiFactory.TenantA, reqId)), new { reason = "Skills gap" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StubConfig.Add(today, 3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), viaConfig.GetProperty("rejection").GetProperty("regretDueBy").GetString());

        // Config down with a fresh tenant: nothing cached, so the local weekends-only calendar answers.
        var tenant = Guid.NewGuid();
        var otherReq = await OpenRequisitionAsync(tenant);
        var application = await CreateAsync(tenant, otherReq);
        api.Config.Down = true;
        try
        {
            var local = await (await api.ClientFor(tenant, RecuroRoles.HrTa).PostAsJsonAsync(url(application), new { reason = "Skills gap" }))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(Domain.Applications.WorkingDays.Add(today, 3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), local.GetProperty("rejection").GetProperty("regretDueBy").GetString());
        }
        finally
        {
            api.Config.Down = false;
        }
    }

    [Fact]
    public async Task The_TAT_scanner_uses_the_Config_TAT_matrix()
    {
        var tenant = Guid.NewGuid();
        var reqId = await OpenRequisitionAsync(tenant);
        var application = await CreateAsync(tenant, reqId);
        var job = api.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<TatScanJob>().Single();

        // Config says sourcing may take 1 working day; 5 calendar days always overrun it, but not the local 7.
        api.Config.SourcingMaxDays = 1;
        api.Clock.Advance(TimeSpan.FromDays(5));
        try
        {
            await job.ScanAllTenantsAsync(CancellationToken.None);
        }
        finally
        {
            api.Clock.Advance(TimeSpan.FromDays(-5));
            api.Config.SourcingMaxDays = 7;
        }

        Assert.Equal(1, await OutboxCountAsync(tenant, EventTypes.Pipeline.TatBreached, AppId(application)));
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenant);
        var envelope = await scope.ServiceProvider.GetRequiredService<PipelineDbContext>().OutboxMessages
            .Where(m => m.TenantId == tenant && m.Type == EventTypes.Pipeline.TatBreached).Select(m => m.Envelope).SingleAsync();
        Assert.Matches("\"escalationPath\": ?\"hrhead\"", envelope);
    }

    [Fact]
    public async Task The_TA_dashboard_fragment_has_the_funnel_and_stage_breaches()
    {
        var tenant = Guid.NewGuid();
        var reqId = await OpenRequisitionAsync(tenant);
        var breached = await CreateAsync(tenant, reqId);
        var screened = await CreateAsync(tenant, reqId);
        await MoveAsync(tenant, screened, "Screened");
        var job = api.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<TatScanJob>().Single();
        api.Clock.Advance(TimeSpan.FromDays(12));
        try
        {
            await job.ScanAllTenantsAsync(CancellationToken.None);
        }
        finally
        {
            api.Clock.Advance(TimeSpan.FromDays(-12));
        }

        var fragment = await api.ClientFor(tenant, RecuroRoles.HrTa).GetFromJsonAsync<JsonElement>("/api/v1/pipeline/dashboard/ta");

        Assert.Equal(["pipeline", "stats", "tatBreaches"], fragment.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        var funnel = fragment.GetProperty("pipeline").EnumerateArray().ToList();
        Assert.Equal(["Sourced", "Screened", "Interview", "Selection", "BGV", "Offer"], funnel.Select(f => f.GetProperty("stage").GetString()));
        Assert.Equal(1, funnel[0].GetProperty("count").GetInt32());
        Assert.Equal(["color", "count", "stage"], funnel[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

        // Both stages overran (7 working days each); rows carry the frontend tatBreaches fields.
        var rows = fragment.GetProperty("tatBreaches").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.GetProperty("position").GetString() == AppId(breached));
        Assert.Equal(["escalation", "link", "position", "reqId", "stage", "stageTone"], rows[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("HR Head", rows[0].GetProperty("escalation").GetString());
        Assert.EndsWith("d / 7d TAT", rows[0].GetProperty("stage").GetString(), StringComparison.Ordinal);
        var tile = Assert.Single(fragment.GetProperty("stats").EnumerateArray());
        Assert.Equal("2", tile.GetProperty("value").GetString());
        Assert.Equal("r", tile.GetProperty("tone").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(tenant, RecuroRoles.Employee).GetAsync("/api/v1/pipeline/dashboard/ta")).StatusCode);
    }

    private static string AppId(JsonElement application) => application.GetProperty("appId").GetString()!;

    private async Task<JsonElement> GetAsync(Guid tenant, string appId) =>
        await api.ClientFor(tenant, RecuroRoles.HrTa).GetFromJsonAsync<JsonElement>($"/api/v1/pipeline/applications/{appId}");

    private async Task<int> OutboxCountAsync(Guid tenant, string type, string appId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenant);
        var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
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
        ActorId = "u-7",
        ActorName = "R. Iyer",
        ActorRole = RecuroRoles.HrHead,
        Data = JsonSerializer.SerializeToElement(data, EventJson.Options),
    };
}
