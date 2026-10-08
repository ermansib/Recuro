using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Interview.Infrastructure.Persistence;

namespace Recuro.Interview.IntegrationTests;

public sealed class InterviewApiTests(InterviewApiFactory api) : IClassFixture<InterviewApiFactory>
{
    private const string Interviews = "/api/v1/interviews";

    private HttpClient HrTa(Guid? tenant = null) => api.ClientFor(tenant ?? InterviewApiFactory.TenantA, RecuroRoles.HrTa, "u-ta", "A. Sharma");

    private HttpClient Panelist(string user) => api.ClientFor(InterviewApiFactory.TenantA, RecuroRoles.Employee, user, user.ToUpperInvariant());

    private async Task MoveAsync(string appId, string reqId, string to, Guid? tenant = null)
    {
        var cloudEvent = new CloudEvent
        {
            Id = Guid.CreateVersion7(),
            Source = "/services/pipeline",
            Type = "pipeline.stage.changed.v1",
            Subject = $"Application/{appId}",
            Time = DateTimeOffset.UtcNow,
            TenantId = tenant ?? InterviewApiFactory.TenantA,
            ActorId = "u-ta",
            ActorName = "A. Sharma",
            ActorRole = RecuroRoles.HrTa,
            Data = JsonSerializer.SerializeToElement(new
            {
                appId,
                reqId,
                candidateId = "CAN-" + appId,
                source = "manual",
                from = "Screened",
                to,
                actor = "A. Sharma",
                at = DateTimeOffset.UtcNow,
                dwellMs = 1000,
            }),
        };
        var processor = api.Services.GetRequiredService<IntegrationEventProcessor>();
        await processor.ProcessAsync(cloudEvent, CancellationToken.None);
        await processor.ProcessAsync(cloudEvent, CancellationToken.None);
    }

    private static object Schedule(string appId, string roundType = "functional", params string[] panel) => new
    {
        appId,
        roundType,
        mode = "Video",
        scheduledFor = DateTimeOffset.UtcNow.AddDays(1),
        durationMinutes = 60,
        panel = (panel.Length == 0 ? ["u-p1"] : panel).Select(p => new { id = p, name = p.ToUpperInvariant() }),
    };

    private static object Feedback(string? reason = null) => new
    {
        ratings = new object[]
        {
            new { competencyId = "credit", score = 4, na = false, comment = "Solid" },
            new { competencyId = "risk", score = 3, na = false, comment = "Fair" },
            new { competencyId = "leadership", score = (int?)null, na = true, comment = "" },
        },
        recommendation = "Recommend",
        justification = "Strong on credit appraisal; risk depth is adequate.",
        flags = new[] { "verify campus claims" },
        reason,
    };

    private async Task<JsonElement> ScheduleAsync(string appId, string reqId = "REQ-2026-0001", params string[] panel)
    {
        await MoveAsync(appId, reqId, "Interview");
        var response = await HrTa().PostAsJsonAsync(Interviews, Schedule(appId, "functional", panel));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<List<string>> OutboxTypesAsync(string subjectPart)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InterviewDbContext>();
        var rows = await db.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).ToListAsync();
        return rows.Where(r => r.Envelope.Contains(subjectPart, StringComparison.Ordinal)).Select(r => r.Type).ToList();
    }

    [Fact]
    public async Task Scheduling_needs_the_interview_stage_and_a_round_from_the_grade_template()
    {
        var early = await HrTa().PostAsJsonAsync(Interviews, Schedule("APP-EARLY"));
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        await MoveAsync("APP-TPL", "REQ-E", "Interview");
        var notAllowed = await HrTa().PostAsJsonAsync(Interviews, Schedule("APP-TPL", "case-study"));
        Assert.Equal(HttpStatusCode.BadRequest, notAllowed.StatusCode);

        await MoveAsync("APP-NOJD", "REQ-NOJD", "Interview");
        var noJd = await HrTa().PostAsJsonAsync(Interviews, Schedule("APP-NOJD"));
        Assert.Equal(HttpStatusCode.Conflict, noJd.StatusCode);

        var asPanelist = await Panelist("u-p1").PostAsJsonAsync(Interviews, Schedule("APP-TPL", "functional"));
        Assert.Equal(HttpStatusCode.Forbidden, asPanelist.StatusCode);
    }

    [Fact]
    public async Task A_scheduled_round_freezes_the_jd_competencies_and_publishes_interview_scheduled()
    {
        var round = await ScheduleAsync("APP-SCH");

        Assert.Equal("Round 1 — Functional Interview", round.GetProperty("round").GetString());
        Assert.Equal(["credit", "risk", "leadership"], round.GetProperty("competencies").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(["case-study.pdf"], round.GetProperty("attachments").EnumerateArray().Select(c => c.GetString()));
        Assert.Contains("interview.scheduled.v1", await OutboxTypesAsync(round.GetProperty("id").GetString()!));
    }

    [Fact]
    public async Task The_panelist_autosaves_and_submits_their_own_form_which_then_locks()
    {
        await ScheduleAsync("APP-FB", "REQ-2026-0001", "u-p1", "u-p2");

        var current = await Panelist("u-p1").GetAsync($"{Interviews}/applications/APP-FB/current");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var form = await current.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Scheduled", form.GetProperty("status").GetString());
        var id = form.GetProperty("id").GetString();
        var etag = current.Headers.ETag!;

        var stranger = await Panelist("u-x").GetAsync($"{Interviews}/assessments/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, stranger.StatusCode);
        var otherPanelist = await Panelist("u-p2").PutAsJsonAsync($"{Interviews}/assessments/{id}", Feedback());
        Assert.Equal(HttpStatusCode.Forbidden, otherPanelist.StatusCode);

        using var save = new HttpRequestMessage(HttpMethod.Put, $"{Interviews}/assessments/{id}") { Content = JsonContent.Create(Feedback()) };
        save.Headers.IfMatch.Add(etag);
        var saved = await Panelist("u-p1").SendAsync(save);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("Draft", (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        using var stale = new HttpRequestMessage(HttpMethod.Put, $"{Interviews}/assessments/{id}") { Content = JsonContent.Create(Feedback()) };
        stale.Headers.IfMatch.Add(etag);
        Assert.Equal(HttpStatusCode.Conflict, (await Panelist("u-p1").SendAsync(stale)).StatusCode);

        var submitted = await Panelist("u-p1").PostAsJsonAsync($"{Interviews}/assessments/{id}/submit", Feedback());
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal("Submitted", (await submitted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var locked = await Panelist("u-p1").PutAsJsonAsync($"{Interviews}/assessments/{id}", Feedback());
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);

        var shortReason = await Panelist("u-p1").PostAsJsonAsync($"{Interviews}/assessments/{id}/supersede", Feedback("typo"));
        Assert.Equal(HttpStatusCode.BadRequest, shortReason.StatusCode);
        var superseded = await Panelist("u-p1").PostAsJsonAsync($"{Interviews}/assessments/{id}/supersede", Feedback("Risk score was entered wrongly."));
        Assert.Equal(HttpStatusCode.OK, superseded.StatusCode);
        Assert.Equal(2, (await OutboxTypesAsync(id!)).Count(t => t == "interview.feedback.submitted.v1"));
    }

    [Fact]
    public async Task Selection_for_a_senior_grade_waits_for_ratification_then_publishes_selection_ratified()
    {
        var round = await ScheduleAsync("APP-SEL");
        var seat = round.GetProperty("panel")[0].GetProperty("assessmentId").GetString();

        var early = await HrTa().PostAsync($"{Interviews}/applications/APP-SEL/selection-summary/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Panelist("u-p1").PostAsJsonAsync($"{Interviews}/assessments/{seat}/submit", Feedback())).StatusCode);

        var summary = await (await HrTa().GetAsync($"{Interviews}/applications/APP-SEL/selection-summary")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(summary.GetProperty("complete").GetBoolean());
        Assert.Equal(3.5m, summary.GetProperty("overallAverage").GetDecimal());
        Assert.Equal(["verify campus claims"], summary.GetProperty("openFlags").EnumerateArray().Select(f => f.GetString()));

        var pdf = await HrTa().GetAsync($"{Interviews}/applications/APP-SEL/selection-summary/pdf");
        Assert.Equal(new MediaTypeHeaderValue("application/pdf"), pdf.Content.Headers.ContentType);
        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync())[..8], StringComparison.Ordinal);

        var submitted = await HrTa().PostAsync($"{Interviews}/applications/APP-SEL/selection-summary/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal("PendingRatification", (await submitted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("selection").GetProperty("status").GetString());
        var (instanceId, start) = api.Workflows.Started.Single(s => s.Start.Subject.Id == "APP-SEL");
        Assert.Equal("SelectionRatification", start.Type);
        Assert.DoesNotContain("interview.selection.ratified.v1", await OutboxTypesAsync("Application/APP-SEL"));

        var cloudEvent = new CloudEvent
        {
            Id = Guid.CreateVersion7(),
            Source = "/services/workflow",
            Type = "workflow.task.completed.v1",
            Subject = $"Task/{Guid.NewGuid()}",
            Time = DateTimeOffset.UtcNow,
            TenantId = InterviewApiFactory.TenantA,
            ActorId = "u-hh",
            ActorName = "K. Mehta",
            ActorRole = RecuroRoles.HrHead,
            Data = JsonSerializer.SerializeToElement(new
            {
                taskId = Guid.NewGuid(),
                instanceId,
                type = "SelectionRatification",
                subjectType = "Application",
                subjectId = "APP-SEL",
                decision = "approve",
                actionId = "approve",
                instanceStatus = "Approved",
            }),
        };
        var processor = api.Services.GetRequiredService<IntegrationEventProcessor>();
        await processor.ProcessAsync(cloudEvent, CancellationToken.None);
        await processor.ProcessAsync(cloudEvent, CancellationToken.None);

        Assert.Single(await OutboxTypesAsync("Application/APP-SEL"), "interview.selection.ratified.v1");
    }

    [Fact]
    public async Task Rejecting_the_application_cancels_open_rounds()
    {
        var round = await ScheduleAsync("APP-REJ");
        await MoveAsync("APP-REJ", "REQ-2026-0001", "Rejected");

        var reloaded = await HrTa().GetFromJsonAsync<JsonElement>($"{Interviews}/{round.GetProperty("id").GetString()}");
        Assert.Equal("Cancelled", reloaded.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Another_tenant_sees_nothing()
    {
        var round = await ScheduleAsync("APP-TEN");

        var other = await HrTa(InterviewApiFactory.TenantB).GetAsync($"{Interviews}/{round.GetProperty("id").GetString()}");
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        var list = await HrTa(InterviewApiFactory.TenantB).GetFromJsonAsync<JsonElement>($"{Interviews}/applications/APP-TEN");
        Assert.Equal(0, list.GetArrayLength());
    }
}
