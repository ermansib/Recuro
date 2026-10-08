using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Infrastructure.Email;

namespace Recuro.Notification.IntegrationTests;

/// <summary>Onboarding (RCU-ONB-001/002/004/005) and KPI pack (RCU-RPT-003) notifications.</summary>
[Collection(NotificationCollection.Name)]
public sealed class OnboardingAndReportTests(NotificationApiFactory api)
{
    private static async Task<string[]> TitlesAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications")).EnumerateArray().Select(i => i.GetProperty("title").GetString()!)];

    private async Task DispatchAsync() =>
        await ActivatorUtilities.CreateInstance<EmailDispatchJob>(api.Services).RunOnceAsync(CancellationToken.None);

    [Fact]
    public async Task The_new_joiner_is_emailed_the_documents_to_bring()
    {
        var tenant = Guid.NewGuid();
        var candidate = Guid.NewGuid();
        api.Backends.Candidates[candidate] = ("A. Rao", "a.rao@example.test");
        await api.PublishAsync(
            "onboarding.joining_instructions_sent.v1",
            new
            {
                onbId = Guid.NewGuid(),
                appId = "APP-21",
                candidateId = candidate,
                joiningDate = "2026-11-02",
                documents = new[] { new { type = "identity-proof", label = "Identity proof", mandatory = true }, new { type = "salary-slips", label = "Last 3 salary slips", mandatory = false } },
            },
            tenant: tenant);
        await DispatchAsync();

        var sent = Assert.Single(api.Mail.Sent, m => m.TenantId == tenant);
        Assert.Equal("a.rao@example.test", sent.ToAddress);
        Assert.Contains("Please bring these documents on your first day: Identity proof, Last 3 salary slips.", sent.Paragraphs);
        Assert.Empty(await TitlesAsync(api.ClientFor(tenant, RecuroRoles.HrTa)));
    }

    [Fact]
    public async Task A_probation_milestone_reaches_its_roles_and_the_reporting_manager()
    {
        var tenant = Guid.NewGuid();
        await api.PublishAsync(
            "onboarding.milestone.due.v1",
            new { onbId = Guid.NewGuid(), appId = "APP-22", milestoneId = Guid.NewGuid(), milestone = "probation-review", label = "Day 75–90 probation review", phase = "Probation", dueOn = "2027-01-15", assigneeRoles = new[] { "hrta" }, reportingManagerId = "mgr-1" },
            tenant: tenant);
        await api.PublishAsync(
            "onboarding.milestone.due.v1",
            new { onbId = Guid.NewGuid(), appId = "APP-23", milestoneId = Guid.NewGuid(), milestone = "engagement-t21", label = "T-21 engagement call", phase = "PreBoarding", dueOn = "2026-10-12", assigneeRoles = new[] { "hrta" } },
            tenant: tenant);

        Assert.Equal(
            ["Day 75–90 probation review — APP-22", "T-21 engagement call — APP-23"],
            (await TitlesAsync(api.ClientFor(tenant, RecuroRoles.HrTa))).Order(StringComparer.Ordinal));
        Assert.Equal(["Day 75–90 probation review — APP-22"], await TitlesAsync(api.ClientFor(tenant, RecuroRoles.Employee, "mgr-1")));
    }

    [Fact]
    public async Task Day_one_readiness_and_the_probation_outcome_reach_HR_TA()
    {
        var tenant = Guid.NewGuid();
        await api.PublishAsync("onboarding.day1.ready.v1", new { onbId = Guid.NewGuid(), appId = "APP-24", joiningDate = "2026-11-02", readyAt = "2026-10-28T10:00:00Z" }, tenant: tenant);
        await api.PublishAsync("onboarding.employee.confirmed.v1", new { onbId = Guid.NewGuid(), appId = "APP-25", confirmedAt = "2027-04-30T10:00:00Z" }, tenant: tenant);
        await api.PublishAsync("onboarding.probation.extended.v1", new { onbId = Guid.NewGuid(), appId = "APP-26", cycle = 2, extendedByMonths = 3, newProbationEnd = "2027-07-31", reason = "Targets not yet met" }, tenant: tenant);

        Assert.Equal(
            ["Day-1 ready — APP-24", "Employee confirmed — APP-25", "Probation extended — APP-26"],
            (await TitlesAsync(api.ClientFor(tenant, RecuroRoles.HrTa))).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_KPI_pack_is_emailed_to_everyone_in_its_role_with_download_links()
    {
        var tenant = Guid.NewGuid();
        var packId = Guid.NewGuid();
        api.Backends.Staff[(tenant, RecuroRoles.MdCeo)] = [new StaffContact("md-1", "V. Shah", "v.shah@example.test"), new StaffContact("md-2", "L. Iyer", "l.iyer@example.test")];
        await api.PublishAsync(
            "reporting.pack.ready.v1",
            new { packId, period = "2026-09", periodLabel = "Sep 2026", cadence = "Monthly", recipientRole = "mdceo", onTrack = "5 / 7", pdfPath = $"/api/v1/reports/packs/{packId}/pdf", csvPath = $"/api/v1/reports/packs/{packId}/csv", snapshotHash = "abc" },
            tenant: tenant);
        await DispatchAsync();

        var sent = api.Mail.Sent.Where(m => m.TenantId == tenant).ToList();
        Assert.Equal(["l.iyer@example.test", "v.shah@example.test"], sent.Select(m => m.ToAddress).Order(StringComparer.Ordinal));
        Assert.All(sent, m =>
        {
            Assert.Equal("📊 Monthly KPI pack — Sep 2026", m.Subject);
            Assert.Contains($"PDF: http://localhost:5100/api/v1/reports/packs/{packId}/pdf", m.Paragraphs);
        });
        Assert.Empty(await TitlesAsync(api.ClientFor(tenant, RecuroRoles.MdCeo)));
    }
}
