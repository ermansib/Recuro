using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Workflow.Infrastructure.Persistence;
using Recuro.Workflow.Infrastructure.Scheduling;

namespace Recuro.Workflow.IntegrationTests;

public sealed class WorkflowApiTests(WorkflowApiFactory api) : IClassFixture<WorkflowApiFactory>
{
    private const string Workflows = "/api/v1/workflows";
    private const string Approvals = "/api/v1/approvals";

    private static object Start(string subjectId, string? key = null, params object[] legs) => new
    {
        type = "MRF",
        subject = new { type = "Requisition", id = subjectId },
        configVersionId = "doa-2026.08",
        correlationKey = key ?? $"requisition:{subjectId}:1",
        legs = legs.Length > 0 ? legs : [Leg("hrhead", 2, new { role = "mdceo", label = "MD/CEO", afterWorkingDays = 1 })],
        presentation = new
        {
            kind = "MRF",
            tone = "default",
            title = $"MRF Approval — {subjectId}",
            meta = "Senior Manager — Credit · HQ — Mumbai",
            route = "Route: HOD → Function Head → HR Head",
            sensitive = new { label = "Band", value = "₹18L – ₹24L" },
            actions = new[]
            {
                new { id = "approve", label = "Approve", style = "primary", effect = "resolve", resultText = (string?)"Approved" },
                new { id = "reject", label = "Reject", style = "danger", effect = "reject", resultText = (string?)null },
                new { id = "query", label = "Raise query", style = "ghost", effect = "query", resultText = (string?)null },
            },
        },
    };

    private static object Leg(string role, int sla = 2, params object[] escalation) => new
    {
        name = $"Leg {role}",
        assignees = new[] { new { role, label = role.ToUpperInvariant() } },
        slaWorkingDays = sla,
        escalation,
    };

    private HttpClient As(string role, Guid? tenant = null) => api.ClientFor(tenant ?? WorkflowApiFactory.TenantA, role);

    private async Task<JsonElement> StartAsync(object body, Guid? tenant = null)
    {
        var response = await As(RecuroRoles.HrTa, tenant).PostAsJsonAsync(Workflows, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> InboxItemAsync(string role, string subjectId, Guid? tenant = null)
    {
        var items = await As(role, tenant).GetFromJsonAsync<JsonElement>(Approvals);
        return items.EnumerateArray().Single(i => i.GetProperty("entity").GetProperty("id").GetString() == subjectId);
    }

    private async Task<List<string>> OutboxTypesAsync(string subjectId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var rows = await db.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).ToListAsync();
        // The test clock is frozen, so events of one test share a timestamp: compare them as a sorted list.
        return rows.Where(r => r.Envelope.Replace(" ", string.Empty, StringComparison.Ordinal).Contains($"\"subjectId\":\"{subjectId}\"", StringComparison.Ordinal)).Select(r => r.Type).Order(StringComparer.Ordinal).ToList();
    }

    [Fact]
    public async Task Starting_is_idempotent_on_the_correlation_key()
    {
        var first = await StartAsync(Start("REQ-2026-1001"));

        var again = await As(RecuroRoles.HrTa).PostAsJsonAsync(Workflows, Start("REQ-2026-1001"));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var body = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(first.GetProperty("id").GetString(), body.GetProperty("id").GetString());
        Assert.Equal("Active", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_invalid_route_is_a_validation_error()
    {
        var response = await As(RecuroRoles.HrTa).PostAsJsonAsync(Workflows, Start("REQ-2026-1002", legs: new { name = "x", assignees = Array.Empty<object>() }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_inbox_returns_the_frontend_approval_item_for_the_assignee_only()
    {
        await StartAsync(Start("REQ-2026-1003"));

        var item = await InboxItemAsync(RecuroRoles.HrHead, "REQ-2026-1003");
        Assert.Equal("hrhead", item.GetProperty("assigneeRole").GetString());
        Assert.Equal("MRF Approval — REQ-2026-1003", item.GetProperty("title").GetString());
        Assert.Equal("⏱ SLA 2d", item.GetProperty("chip").GetProperty("text").GetString());
        Assert.Equal("₹18L – ₹24L", item.GetProperty("sensitive").GetProperty("value").GetString());
        Assert.Equal("resolve", item.GetProperty("actions")[0].GetProperty("effect").GetString());
        Assert.True(item.GetProperty("isNew").GetBoolean());
        Assert.False(item.TryGetProperty("decision", out _));

        var hrta = await As(RecuroRoles.HrTa).GetFromJsonAsync<JsonElement>(Approvals);
        Assert.DoesNotContain(hrta.EnumerateArray(), i => i.GetProperty("entity").GetProperty("id").GetString() == "REQ-2026-1003");
    }

    [Fact]
    public async Task Tenants_never_see_each_others_approvals()
    {
        await StartAsync(Start("REQ-2026-1004"), WorkflowApiFactory.TenantA);
        var itemId = (await InboxItemAsync(RecuroRoles.HrHead, "REQ-2026-1004")).GetProperty("id").GetString();

        var other = await As(RecuroRoles.HrHead, WorkflowApiFactory.TenantB).GetFromJsonAsync<JsonElement>(Approvals);
        Assert.DoesNotContain(other.EnumerateArray(), i => i.GetProperty("id").GetString() == itemId);

        var decide = await As(RecuroRoles.HrHead, WorkflowApiFactory.TenantB)
            .PostAsJsonAsync($"{Approvals}/{itemId}/decision", new { actionId = "approve" });
        Assert.Equal(HttpStatusCode.NotFound, decide.StatusCode);
    }

    [Fact]
    public async Task Deciding_follows_the_rules()
    {
        await StartAsync(Start("REQ-2026-1005"));
        var id = (await InboxItemAsync(RecuroRoles.HrHead, "REQ-2026-1005")).GetProperty("id").GetString();
        var url = $"{Approvals}/{id}/decision";

        var notMine = await As(RecuroRoles.HrTa).PostAsJsonAsync(url, new { actionId = "approve" });
        Assert.Equal(HttpStatusCode.Forbidden, notMine.StatusCode);

        var noReason = await As(RecuroRoles.HrHead).PostAsJsonAsync(url, new { actionId = "reject", reason = "no" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var rejected = await As(RecuroRoles.HrHead).PostAsJsonAsync(url, new { actionId = "reject", reason = "Budget not available this quarter." });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var item = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Budget not available this quarter.", item.GetProperty("decision").GetProperty("reason").GetString());
        Assert.Equal("✓ Decided", item.GetProperty("chip").GetProperty("text").GetString());

        var again = await As(RecuroRoles.HrHead).PostAsJsonAsync(url, new { actionId = "approve" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        Assert.Equal(
            ["workflow.task.completed.v1", "workflow.task.created.v1"],
            await OutboxTypesAsync("REQ-2026-1005"));
    }

    [Fact]
    public async Task Approving_every_leg_completes_the_workflow()
    {
        var started = await StartAsync(Start("REQ-2026-1006", legs: [Leg("hrhead"), Leg("mdceo", 3)]));
        var first = (await InboxItemAsync(RecuroRoles.HrHead, "REQ-2026-1006")).GetProperty("id").GetString();

        await As(RecuroRoles.HrHead).PostAsJsonAsync($"{Approvals}/{first}/decision", new { actionId = "approve" });
        var second = await InboxItemAsync(RecuroRoles.MdCeo, "REQ-2026-1006");
        Assert.Equal("••••", second.GetProperty("sensitive").GetProperty("value").GetString());

        var done = await As(RecuroRoles.MdCeo).PostAsJsonAsync($"{Approvals}/{second.GetProperty("id").GetString()}/decision", new { actionId = "approve" });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);

        var instance = await As(RecuroRoles.HrTa).GetFromJsonAsync<JsonElement>($"{Workflows}/{started.GetProperty("id").GetString()}");
        Assert.Equal("Approved", instance.GetProperty("status").GetString());
        Assert.Equal(2, instance.GetProperty("tasks").GetArrayLength());
    }

    [Fact]
    public async Task A_query_pauses_the_sla_until_resumed()
    {
        await StartAsync(Start("REQ-2026-1007"));
        var id = (await InboxItemAsync(RecuroRoles.HrHead, "REQ-2026-1007")).GetProperty("id").GetString();

        var queried = await As(RecuroRoles.HrHead).PostAsJsonAsync($"{Approvals}/{id}/decision", new { actionId = "query", reason = "Why two positions?" });
        var item = await queried.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("⏸ Query open · SLA paused", item.GetProperty("chip").GetProperty("text").GetString());

        var resumed = await As(RecuroRoles.HrTa).PostAsync($"{Approvals}/{id}/resume", null);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await As(RecuroRoles.HrTa).PostAsync($"{Approvals}/{id}/resume", null)).StatusCode);
        Assert.Equal(
            ["workflow.sla.paused.v1", "workflow.sla.resumed.v1", "workflow.task.created.v1"],
            await OutboxTypesAsync("REQ-2026-1007"));
    }

    [Fact]
    public async Task Cancelling_withdraws_the_item_and_is_idempotent()
    {
        var started = await StartAsync(Start("REQ-2026-1008"));
        var url = $"{Workflows}/{started.GetProperty("id").GetString()}/cancel";

        Assert.Equal(HttpStatusCode.OK, (await As(RecuroRoles.HrTa).PostAsJsonAsync(url, new { reason = "Submit failed." })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await As(RecuroRoles.HrTa).PostAsJsonAsync(url, new { reason = "Submit failed." })).StatusCode);

        var inbox = await As(RecuroRoles.HrHead).GetFromJsonAsync<JsonElement>(Approvals);
        Assert.DoesNotContain(inbox.EnumerateArray(), i => i.GetProperty("entity").GetProperty("id").GetString() == "REQ-2026-1008");

        // The key is free again: a resubmit opens a fresh workflow.
        await StartAsync(Start("REQ-2026-1008"));
    }

    [Fact]
    public async Task Overdue_tasks_escalate_once()
    {
        await StartAsync(Start("REQ-2026-1009"));
        var scheduler = api.Services.GetRequiredService<EscalationScheduler>();
        api.Clock.Advance(TimeSpan.FromDays(4));
        try
        {
            Assert.True(await scheduler.RunOnceAsync(CancellationToken.None) >= 1);
            await scheduler.RunOnceAsync(CancellationToken.None);
        }
        finally
        {
            api.Clock.Advance(TimeSpan.FromDays(-4));
        }

        var item = await InboxItemAsync(RecuroRoles.HrHead, "REQ-2026-1009");
        Assert.Equal("⚑ Escalated L1", item.GetProperty("chip").GetProperty("text").GetString());
        var types = await OutboxTypesAsync("REQ-2026-1009");
        Assert.Single(types, t => t == "workflow.escalated.v1");
        Assert.Equal(2, types.Count(t => t == "workflow.task.reminder_due.v1"));

        var count = await As(RecuroRoles.HrHead).GetFromJsonAsync<JsonElement>($"{Approvals}/count");
        Assert.True(count.GetProperty("escalated").GetInt32() >= 1);
    }

    [Fact]
    public async Task Candidates_and_employees_have_no_inbox()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await As(RecuroRoles.Employee).GetAsync(Approvals)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await As(RecuroRoles.Candidate).PostAsJsonAsync(Workflows, Start("REQ-2026-1010"))).StatusCode);
    }
}
