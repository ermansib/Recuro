using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Reporting.Domain.Periods;
using Recuro.Reporting.Infrastructure.Jobs;
using Recuro.Reporting.Infrastructure.Persistence;

namespace Recuro.Reporting.IntegrationTests;

public sealed class ReportingApiTests(ReportingApiFactory api) : IClassFixture<ReportingApiFactory>
{
    /// <summary>The month that just closed, so every test reads a finished period.</summary>
    private static readonly ReportPeriod LastMonth = ReportPeriod.Containing(Cadence.Monthly, DateOnly.FromDateTime(DateTime.UtcNow)).Previous();

    private static DateTimeOffset Day(int offset) =>
        new DateTimeOffset(LastMonth.FirstDay.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero).AddDays(offset);

    [Fact]
    public async Task Hires_flow_from_events_into_the_kpi_register()
    {
        var tenant = Guid.NewGuid();
        await HireAsync(tenant, "1", "referral");

        var register = await GetAsync(tenant, RecuroRoles.HrHead, $"/api/v1/reports/kpis?period={LastMonth.Key}");

        Assert.Equal(LastMonth.Key, register.GetProperty("period").GetString());
        Assert.Equal(1, register.GetProperty("hires").GetInt32());
        var fill = Kpi(register, "time-to-fill");
        Assert.Equal(18m, fill.GetProperty("actual").GetDecimal()); // approved day 2, accepted day 20
        Assert.Equal("18 d", fill.GetProperty("value").GetString());
        Assert.Equal(28m, fill.GetProperty("targetValue").GetDecimal()); // 20 working days from Config
        Assert.Equal("green", fill.GetProperty("tone").GetString());
        Assert.Equal(1m, Kpi(register, "mrf-tat").GetProperty("actual").GetDecimal());
        Assert.Equal("no data", Kpi(register, "quality-of-hire").GetProperty("status").GetString());
        Assert.Equal(9, register.GetProperty("kpis").GetArrayLength());
    }

    [Fact]
    public async Task Redelivered_events_are_counted_once_and_tenants_do_not_mix()
    {
        var tenant = Guid.NewGuid();
        var accepted = Event(tenant, EventTypes.Offer.Accepted, new { appId = "APP-dup", reqId = "REQ-dup", candidateId = "CAN-dup" }, Day(20));
        await HireAsync(tenant, "dup", "portal", accepted);
        await ProcessAsync(accepted);
        await ProcessAsync(accepted with { });

        var register = await GetAsync(tenant, RecuroRoles.HrHead, $"/api/v1/reports/kpis?period={LastMonth.Key}");
        Assert.Equal(1, register.GetProperty("hires").GetInt32());

        var other = await GetAsync(Guid.NewGuid(), RecuroRoles.HrHead, $"/api/v1/reports/kpis?period={LastMonth.Key}");
        Assert.Equal(0, other.GetProperty("hires").GetInt32());

        var status = await GetAsync(tenant, RecuroRoles.HrHead, "/api/v1/reports/projections");
        Assert.Equal(status.GetProperty("lastSequence").GetInt64(), status.GetProperty("checkpoint").GetInt64());
    }

    [Fact]
    public async Task Replay_rebuilds_the_same_projections_from_the_log()
    {
        var tenant = Guid.NewGuid();
        await HireAsync(tenant, "r1", "referral");
        await HireAsync(tenant, "r2", "consultant");
        var before = await GetAsync(tenant, RecuroRoles.HrHead, $"/api/v1/reports/kpis?period={LastMonth.Key}");

        var replay = await api.ClientFor(tenant, RecuroRoles.HrHead).PostAsJsonAsync("/api/v1/reports/projections/replay", new { reset = true });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var result = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("applied").GetInt32() >= 10);

        var after = await GetAsync(tenant, RecuroRoles.HrHead, $"/api/v1/reports/kpis?period={LastMonth.Key}");
        Assert.Equal(before.GetProperty("kpis").ToString(), after.GetProperty("kpis").ToString());
    }

    [Fact]
    public async Task Source_mix_and_cost_per_hire_use_recorded_spend_and_hide_it_from_hr_ta()
    {
        var tenant = Guid.NewGuid();
        await HireAsync(tenant, "c1", "consultant");
        await HireAsync(tenant, "c2", "consultant");
        var cost = await api.ClientFor(tenant, RecuroRoles.HrHead).PostAsJsonAsync(
            "/api/v1/reports/costs",
            new { month = LastMonth.Key, source = "consultant", amount = 84_000m, currency = "inr", note = "Two placements" });
        Assert.Equal(HttpStatusCode.Created, cost.StatusCode);

        var head = await GetAsync(tenant, RecuroRoles.HrHead, $"/api/v1/reports/source-mix?period={LastMonth.Key}");
        var channel = head.GetProperty("sources")[0];
        Assert.Equal("consultant", channel.GetProperty("source").GetString());
        Assert.Equal(42_000m, channel.GetProperty("costPerHire").GetDecimal());
        Assert.Equal("₹42K", Kpi(await GetAsync(tenant, RecuroRoles.HrHead, $"/api/v1/reports/kpis?period={LastMonth.Key}"), "cost-per-hire").GetProperty("value").GetString());

        var ta = await GetAsync(tenant, RecuroRoles.HrTa, $"/api/v1/reports/source-mix?period={LastMonth.Key}");
        Assert.False(ta.GetProperty("sources")[0].TryGetProperty("costPerHire", out _));

        var forbidden = await api.ClientFor(tenant, RecuroRoles.HrTa).PostAsJsonAsync(
            "/api/v1/reports/costs",
            new { month = LastMonth.Key, source = "portal", amount = 1m, currency = "INR" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Only_hr_staff_read_reports()
    {
        var tenant = Guid.NewGuid();
        foreach (var role in new[] { RecuroRoles.Employee, RecuroRoles.Candidate })
        {
            var response = await api.ClientFor(tenant, role).GetAsync("/api/v1/reports/kpis");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        var mdceo = await api.ClientFor(tenant, RecuroRoles.MdCeo).GetAsync("/api/v1/reports/kpis");
        Assert.Equal(HttpStatusCode.OK, mdceo.StatusCode);
        var replay = await api.ClientFor(tenant, RecuroRoles.MdCeo).PostAsJsonAsync("/api/v1/reports/projections/replay", new { reset = true });
        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);

        var badPeriod = await api.ClientFor(tenant, RecuroRoles.HrHead).GetAsync("/api/v1/reports/kpis?period=2026-13");
        Assert.Equal(HttpStatusCode.BadRequest, badPeriod.StatusCode);
    }

    [Fact]
    public async Task Funnel_counts_applications_by_furthest_stage()
    {
        var tenant = Guid.NewGuid();
        await HireAsync(tenant, "f1", "referral");
        await ProcessAsync(Event(tenant, EventTypes.Pipeline.ApplicationCreated, new { appId = "APP-f2", reqId = "REQ-f1", candidateId = "CAN-f2", source = "portal" }, Day(3)));

        var from = Day(-1).ToString("O");
        var to = Day(25).ToString("O");
        var funnel = await GetAsync(tenant, RecuroRoles.HrTa, $"/api/v1/reports/funnel?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        var counts = funnel.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("count").GetInt32()).ToList();
        Assert.Equal([2, 1, 1, 1, 1, 0], counts);
    }

    [Fact]
    public async Task Ta_dashboard_fragment_has_the_six_card_kpis_in_mock_order()
    {
        var fragment = await GetAsync(Guid.NewGuid(), RecuroRoles.HrTa, "/api/v1/reports/dashboard/ta");
        var names = fragment.GetProperty("kpis").EnumerateArray().Select(k => k.GetProperty("name").GetString()).ToList();
        Assert.Equal(6, names.Count);
        Assert.Equal("Offer-to-Join Ratio", names[0]);
        Assert.Equal("Cost per Hire", names[5]); // HR-TA keeps CPH; only channel spend is hidden
    }

    [Fact]
    public async Task Snapshots_freeze_with_a_stable_hash_and_new_revisions()
    {
        var tenant = Guid.NewGuid();
        await HireAsync(tenant, "s1", "referral");
        var client = api.ClientFor(tenant, RecuroRoles.HrHead);

        var first = await (await client.PostAsJsonAsync($"/api/v1/reports/snapshots/{LastMonth.Key}/recompute", new { reason = "month close" })).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await client.PostAsJsonAsync($"/api/v1/reports/snapshots/{LastMonth.Key}/recompute", new { reason = "audit" })).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, first.GetProperty("revision").GetInt32());
        Assert.Equal(2, second.GetProperty("revision").GetInt32());
        Assert.Equal(first.GetProperty("hash").GetString(), second.GetProperty("hash").GetString());

        // A late event changes the live view but not the frozen register until it is recomputed.
        await HireAsync(tenant, "s2", "portal");
        var frozen = await GetAsync(tenant, RecuroRoles.HrHead, $"/api/v1/reports/kpis?period={LastMonth.Key}");
        Assert.Equal(1, frozen.GetProperty("hires").GetInt32());
        Assert.Equal(2, frozen.GetProperty("snapshot").GetProperty("revision").GetInt32());

        var future = await client.PostAsJsonAsync($"/api/v1/reports/snapshots/{LastMonth.Key[..4]}-12/recompute".Replace(LastMonth.Key[..4], (LastMonth.FirstDay.Year + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal), new { });
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
    }

    [Fact]
    public async Task Packs_are_archived_queued_to_notification_and_marked_delivered()
    {
        var tenant = Guid.NewGuid();
        await HireAsync(tenant, "p1", "referral");
        var head = api.ClientFor(tenant, RecuroRoles.HrHead);

        var created = await head.PostAsJsonAsync("/api/v1/reports/packs", new { period = LastMonth.Key, send = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var pack = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("hrhead", pack.GetProperty("recipientRole").GetString());
        Assert.Equal("Queued", pack.GetProperty("delivery").GetString());

        var pdf = await head.GetAsync(pack.GetProperty("pdfUrl").GetString());
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", await pdf.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var csv = await head.GetStringAsync(pack.GetProperty("csvUrl").GetString());
        Assert.Contains("Time to Fill", csv, StringComparison.Ordinal);

        // HR-TA cannot open a pack addressed to HR Head.
        var ta = await api.ClientFor(tenant, RecuroRoles.HrTa).GetAsync(pack.GetProperty("pdfUrl").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, ta.StatusCode);

        var readyId = await ReadyEventIdAsync(tenant);
        await ProcessAsync(Event(tenant, EventTypes.Notification.EmailDispatched, new { templateKey = "report.pack", status = "Sent", sourceEventId = readyId.ToString() }, DateTimeOffset.UtcNow));

        var packs = await GetAsync(tenant, RecuroRoles.HrHead, "/api/v1/reports/packs");
        Assert.Equal("Sent", packs[0].GetProperty("delivery").GetString());
    }

    [Fact]
    public async Task Schedule_sends_each_closed_period_once()
    {
        var tenant = Guid.NewGuid();
        await HireAsync(tenant, "j1", "referral");
        var job = api.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<ReportScheduleJob>().Single();

        Assert.True(await job.RunAllTenantsAsync(CancellationToken.None));
        Assert.True(await job.RunAllTenantsAsync(CancellationToken.None));

        var packs = await GetAsync(tenant, RecuroRoles.HrHead, "/api/v1/reports/packs");
        var keys = packs.EnumerateArray().Select(p => (p.GetProperty("period").GetString(), p.GetProperty("recipientRole").GetString())).ToList();
        Assert.Equal(2, keys.Count);
        Assert.Contains((LastMonth.Key, "hrhead"), keys);
        Assert.Contains(keys, k => k.Item2 == "mdceo" && k.Item1!.Contains('Q', StringComparison.Ordinal));
    }

    [Fact]
    public async Task Definitions_publish_new_versions_and_reject_unknown_metrics()
    {
        var tenant = Guid.NewGuid();
        var head = api.ClientFor(tenant, RecuroRoles.HrHead);
        var current = await GetAsync(tenant, RecuroRoles.HrHead, "/api/v1/reports/definitions");
        Assert.Equal(0, current.GetProperty("version").GetInt32());

        var definitions = current.GetProperty("definitions").EnumerateArray()
            .Select(d => JsonSerializer.Deserialize<Dictionary<string, object?>>(d.GetRawText())!)
            .ToList();
        var published = await head.PostAsJsonAsync("/api/v1/reports/definitions", new { definitions });
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        Assert.Equal(1, (await published.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32());

        var bad = await head.PostAsJsonAsync("/api/v1/reports/definitions", new { definitions = new[] { new { key = "vibes", name = "Vibes", description = "x", unit = "percent", direction = "atLeast", targetLabel = "t" } } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Purged_candidates_leave_counts_but_lose_their_identity()
    {
        var tenant = Guid.NewGuid();
        await HireAsync(tenant, "g1", "referral");
        await ProcessAsync(Event(tenant, EventTypes.Candidate.Purged, new { candidateId = "CAN-g1", purgeScope = "full" }, DateTimeOffset.UtcNow));

        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenant);
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var fact = await db.Applications.SingleAsync(a => a.AppId == "APP-g1");
        Assert.Null(fact.CandidateId);
        Assert.Equal(1, (await GetAsync(tenant, RecuroRoles.HrHead, $"/api/v1/reports/kpis?period={LastMonth.Key}")).GetProperty("hires").GetInt32());
    }

    private async Task HireAsync(Guid tenant, string id, string source, CloudEvent? accepted = null)
    {
        var req = $"REQ-{id}";
        var app = $"APP-{id}";
        var candidate = $"CAN-{id}";
        await ProcessAsync(Event(tenant, EventTypes.Requisition.Submitted, new { reqId = req, grade = "M1", routeRef = "doa-m1", configVersionId = "cv-1", budgetStatus = "in", from = "Draft", to = "PendingApproval" }, Day(1)));
        await ProcessAsync(Event(tenant, EventTypes.Requisition.Approved, new { reqId = req, decision = "approved", configVersionId = "cv-1", from = "PendingApproval", to = "Approved" }, Day(2)));
        await ProcessAsync(Event(tenant, EventTypes.Pipeline.ApplicationCreated, new { appId = app, reqId = req, candidateId = candidate, source }, Day(3)));
        foreach (var (stage, day) in new[] { ("Screened", 5), ("Interview", 8), ("BGV", 12), ("Offer", 15) })
        {
            await ProcessAsync(Event(tenant, EventTypes.Pipeline.StageChanged, new { appId = app, reqId = req, candidateId = candidate, source, to = stage, at = Day(day) }, Day(day)));
        }

        await ProcessAsync(accepted ?? Event(tenant, EventTypes.Offer.Accepted, new { appId = app, reqId = req, candidateId = candidate }, Day(20)));
    }

    private Task ProcessAsync(CloudEvent cloudEvent) =>
        api.Services.GetRequiredService<IntegrationEventProcessor>().ProcessAsync(cloudEvent, CancellationToken.None);

    private async Task<JsonElement> GetAsync(Guid tenant, string role, string path)
    {
        var response = await api.ClientFor(tenant, role).GetAsync(path);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> ReadyEventIdAsync(Guid tenant)
    {
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenant);
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        return await db.OutboxMessages.Where(m => m.TenantId == tenant && m.Type == "reporting.pack.ready.v1").Select(m => m.Id).SingleAsync();
    }

    private static JsonElement Kpi(JsonElement register, string key) =>
        register.GetProperty("kpis").EnumerateArray().Single(k => k.GetProperty("key").GetString() == key);

    private static CloudEvent Event(Guid tenant, string type, object data, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        Source = "/services/test",
        Type = type,
        Subject = "test",
        Time = at,
        TenantId = tenant,
        ActorId = "u-9",
        ActorName = "R. Menon",
        ActorRole = RecuroRoles.HrTa,
        Data = JsonSerializer.SerializeToElement(data, EventJson.Options),
    };
}
