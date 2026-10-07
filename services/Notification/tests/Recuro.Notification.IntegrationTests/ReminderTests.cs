using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Notification.Infrastructure.Email;

namespace Recuro.Notification.IntegrationTests;

/// <summary>Reminder and chase events (RCU-WFL-003, RCU-INT-004, RCU-OFR-006).</summary>
[Collection(NotificationCollection.Name)]
public sealed class ReminderTests(NotificationApiFactory api)
{
    private static async Task<JsonElement[]> BellAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications")).EnumerateArray().ToArray();

    private static string[] Titles(IEnumerable<JsonElement> items) =>
        [.. items.Select(i => i.GetProperty("title").GetString()!)];

    [Fact]
    public async Task A_task_reminder_reaches_its_assignees_only()
    {
        var tenant = Guid.NewGuid();
        await api.PublishAsync(
            "workflow.task.reminder_due.v1",
            new
            {
                taskId = Guid.NewGuid(),
                instanceId = Guid.NewGuid(),
                leg = "HR Head",
                subjectType = "Requisition",
                subjectId = "REQ-2026-0201",
                assigneeIds = new[] { "head-1" },
                thresholdPercent = 50,
                dueAt = "2026-10-09T12:00:00Z",
            },
            tenant: tenant,
            subject: "WorkflowTask/t-1");

        var item = Assert.Single(await BellAsync(api.ClientFor(tenant, RecuroRoles.HrHead, "head-1")));
        Assert.Equal("Approval reminder — REQ-2026-0201", item.GetProperty("title").GetString());
        Assert.Equal("50% of the SLA used · due 2026-10-09T12:00:00Z.", item.GetProperty("body").GetString());
        Assert.Empty(await BellAsync(api.ClientFor(tenant, RecuroRoles.HrHead, "head-2")));
    }

    [Fact]
    public async Task A_feedback_reminder_reaches_interviewers_who_have_not_submitted()
    {
        var tenant = Guid.NewGuid();
        await api.PublishAsync(
            "interview.feedback.reminder_due.v1",
            new
            {
                interviewId = Guid.NewGuid(),
                appId = "APP-9",
                reqId = "REQ-9",
                round = "R2",
                pendingInterviewerIds = new[] { "int-1", "int-2" },
                endedAt = "2026-10-06T10:00:00Z",
                overdueAt = "2026-10-08T10:00:00Z",
            },
            tenant: tenant);

        Assert.Equal(["Feedback reminder — APP-9"], Titles(await BellAsync(api.ClientFor(tenant, RecuroRoles.HrTa, "int-1"))));
        Assert.Equal(["Feedback reminder — APP-9"], Titles(await BellAsync(api.ClientFor(tenant, RecuroRoles.HrTa, "int-2"))));
        Assert.Empty(await BellAsync(api.ClientFor(tenant, RecuroRoles.HrTa, "int-3")));
    }

    [Fact]
    public async Task An_offer_chase_emails_the_candidate_and_tells_HR_TA()
    {
        var tenant = Guid.NewGuid();
        await api.PublishAsync(
            "offer.chase_due.v1",
            new
            {
                offerId = "OFF-7",
                appId = "APP-7",
                reqId = "REQ-7",
                candidateId = "CND-7",
                sentAt = "2026-10-01T09:00:00Z",
                chaseNumber = 1,
                expiresAt = "2026-10-15T23:59:59Z",
            },
            tenant: tenant);
        await ActivatorUtilities.CreateInstance<EmailDispatchJob>(api.Services).RunOnceAsync(CancellationToken.None);

        var sent = Assert.Single(api.Mail.Sent, m => m.TenantId == tenant);
        Assert.Equal("cnd-7@example.test", sent.ToAddress);
        Assert.Equal("Your offer is awaiting your response", sent.Subject);

        var item = Assert.Single(await BellAsync(api.ClientFor(tenant, RecuroRoles.HrTa)));
        Assert.Equal("Offer awaiting response — OFF-7", item.GetProperty("title").GetString());
        Assert.Equal("Chase #1 sent · expires 2026-10-15T23:59:59Z.", item.GetProperty("body").GetString());
    }
}
