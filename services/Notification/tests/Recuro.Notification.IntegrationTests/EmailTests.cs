using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Application.Directory;
using Recuro.Notification.Infrastructure.Email;
using Recuro.Notification.Infrastructure.Persistence;

namespace Recuro.Notification.IntegrationTests;

[Collection(NotificationCollection.Name)]
public sealed class EmailTests(NotificationApiFactory api)
{
    private async Task ProvisionAsync(Guid tenant, string userId, string email, params string[] roles) =>
        await api.PublishAsync("identity.user.provisioned.v1", new { userId, name = $"User {userId}", email, roles }, tenant: tenant, subject: $"User/{userId}");

    private async Task<int> DispatchAsync() =>
        await ActivatorUtilities.CreateInstance<EmailDispatchJob>(api.Services).RunOnceAsync(CancellationToken.None);

    private async Task<List<string>> OutboxTypesAsync(Guid tenant)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        return await db.OutboxMessages.Where(m => m.TenantId == tenant).Select(m => m.Type).ToListAsync();
    }

    [Fact]
    public async Task An_adverse_BGV_emails_each_HR_Head_and_MD_and_logs_the_delivery()
    {
        var tenant = Guid.NewGuid();
        await ProvisionAsync(tenant, "head-1", "head1@example.test", RecuroRoles.HrHead);
        await ProvisionAsync(tenant, "head-2", "head2@example.test", RecuroRoles.HrHead);
        await ProvisionAsync(tenant, "md-1", "md@example.test", RecuroRoles.MdCeo);

        await api.PublishAsync("bgv.adverse.flagged.v1", new { appId = "APP-77", checkType = "Employment" }, tenant: tenant);
        await DispatchAsync();

        var sentTo = api.Mail.Sent.Where(m => m.TenantId == tenant).Select(m => m.ToAddress).Order().ToList();
        Assert.Equal(["head1@example.test", "head2@example.test", "md@example.test"], sentTo);

        var emails = await api.ClientFor(tenant, RecuroRoles.HrHead, "head-1").GetFromJsonAsync<JsonElement>("/api/v1/notifications/emails");
        var email = Assert.Single(emails.EnumerateArray());
        Assert.Equal(
            ["createdAt", "cta", "from", "id", "paragraphs", "recipientRole", "signature", "subject", "tag", "to", "unread"],
            email.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("head1@example.test", email.GetProperty("to").GetString());
        Assert.Equal("⚑ Adverse BGV finding — APP-77", email.GetProperty("subject").GetString());
        Assert.Equal("Recuro <no-reply@recuro.local>", email.GetProperty("from").GetString());

        var log = await api.ClientFor(tenant, RecuroRoles.HrHead).GetFromJsonAsync<JsonElement>("/api/v1/notifications/delivery-log?status=sent");
        Assert.Equal(3, log.GetArrayLength());
        Assert.All(log.EnumerateArray(), row =>
        {
            Assert.Equal("bgv.adverse.flagged", row.GetProperty("templateKey").GetString());
            Assert.False(string.IsNullOrEmpty(row.GetProperty("providerMessageId").GetString()));
        });

        var outbox = await OutboxTypesAsync(tenant);
        Assert.Equal(3, outbox.Count(t => t == "notification.email.dispatched.v1"));
        Assert.Equal(2, outbox.Count(t => t == "notification.created.v1"));
    }

    [Fact]
    public async Task Without_an_address_the_email_is_logged_as_suppressed_not_sent()
    {
        var tenant = Guid.NewGuid();

        await api.PublishAsync("offer.approved.v1", new { offerId = "OFF-1", appId = "APP-1" }, tenant: tenant);
        await DispatchAsync();

        Assert.DoesNotContain(api.Mail.Sent, m => m.TenantId == tenant);
        var row = Assert.Single((await api.ClientFor(tenant, RecuroRoles.HrHead).GetFromJsonAsync<JsonElement>("/api/v1/notifications/delivery-log")).EnumerateArray());
        Assert.Equal("Suppressed", row.GetProperty("status").GetString());
        Assert.Equal("no_recipient_address", row.GetProperty("lastError").GetString());
    }

    [Fact]
    public async Task Three_failed_attempts_dead_letter_the_email_and_announce_it()
    {
        var tenant = Guid.NewGuid();
        await ProvisionAsync(tenant, "ta-1", "flaky@example.test", RecuroRoles.HrTa);
        api.Mail.FailFor["flaky@example.test"] = true;

        await api.PublishAsync("offer.approved.v1", new { offerId = "OFF-2", appId = "APP-2" }, tenant: tenant);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await DispatchAsync();
        }

        var row = Assert.Single((await api.ClientFor(tenant, RecuroRoles.HrHead).GetFromJsonAsync<JsonElement>("/api/v1/notifications/delivery-log")).EnumerateArray());
        Assert.Equal("Failed", row.GetProperty("status").GetString());
        Assert.Equal(3, row.GetProperty("attempts").GetInt32());
        Assert.Contains("notification.email.failed.v1", await OutboxTypesAsync(tenant));
    }

    [Fact]
    public async Task A_bounced_address_stops_receiving_mail()
    {
        var tenant = Guid.NewGuid();
        await ProvisionAsync(tenant, "ta-2", "Gone@Example.test", RecuroRoles.HrTa);

        var bounce = await api.ClientFor(tenant, RecuroRoles.Service, "svc-mail").PostAsJsonAsync("/api/v1/notifications/email-bounces", new { address = "gone@example.test", reason = "550 no such user" });
        await api.PublishAsync("offer.approved.v1", new { offerId = "OFF-3", appId = "APP-3" }, tenant: tenant);
        await DispatchAsync();

        Assert.Equal(HttpStatusCode.NoContent, bounce.StatusCode);
        Assert.DoesNotContain(api.Mail.Sent, m => m.TenantId == tenant);
        var row = Assert.Single((await api.ClientFor(tenant, RecuroRoles.HrHead).GetFromJsonAsync<JsonElement>("/api/v1/notifications/delivery-log")).EnumerateArray());
        Assert.Equal("address_bounced", row.GetProperty("lastError").GetString());
    }

    [Fact]
    public async Task Only_services_report_bounces_and_only_HR_staff_read_the_delivery_log()
    {
        var bounce = await api.ClientFor(NotificationApiFactory.TenantA, RecuroRoles.HrTa).PostAsJsonAsync("/api/v1/notifications/email-bounces", new { address = "x@example.test" });
        var log = await api.ClientFor(NotificationApiFactory.TenantA, RecuroRoles.Candidate).GetAsync("/api/v1/notifications/delivery-log");

        Assert.Equal(HttpStatusCode.Forbidden, bounce.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, log.StatusCode);
    }

    [Fact]
    public async Task Emails_are_marked_read_per_person()
    {
        var tenant = Guid.NewGuid();
        await ProvisionAsync(tenant, "ta-3", "ta3@example.test", RecuroRoles.HrTa);
        await api.PublishAsync("offer.approved.v1", new { offerId = "OFF-4", appId = "APP-4" }, tenant: tenant);
        var client = api.ClientFor(tenant, RecuroRoles.HrTa, "ta-3");

        await client.PostAsJsonAsync("/api/v1/notifications/emails/read", new { all = true });

        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/emails?filter=unread")).EnumerateArray());
        Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/emails")).EnumerateArray());
    }

    [Fact]
    public async Task Replicas_never_claim_the_same_email()
    {
        var tenant = Guid.NewGuid();
        await ProvisionAsync(tenant, "ta-4", "ta4@example.test", RecuroRoles.HrTa);
        await api.PublishAsync("offer.approved.v1", new { offerId = "OFF-5", appId = "APP-5" }, tenant: tenant);

        await Task.WhenAll(DispatchAsync(), DispatchAsync(), DispatchAsync());

        Assert.Single(api.Mail.Sent, m => m.TenantId == tenant);
    }

    [Fact]
    public async Task Tenant_scope_is_required_to_read_emails()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(null);

        Assert.Empty(await db.Emails.ToListAsync());
    }

    [Fact]
    public async Task The_regret_email_waits_for_the_date_Pipeline_set()
    {
        var tenant = Guid.NewGuid();
        var candidate = Guid.NewGuid();
        api.Backends.Candidates[candidate] = ("Asha Rao", "asha.rao@example.test");
        var sendOn = DateTime.UtcNow.Date.AddDays(3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        await api.PublishAsync(
            "pipeline.application.final_rejected.v1",
            new { appId = "APP-R1", reqId = "REQ-1", candidateId = candidate, reason = "Not shortlisted", regretSendAt = sendOn, retainUntil = "2027-10-07" },
            tenant: tenant);
        await DispatchAsync();

        Assert.DoesNotContain(api.Mail.Sent, m => m.TenantId == tenant);
        var row = Assert.Single((await api.ClientFor(tenant, RecuroRoles.HrHead).GetFromJsonAsync<JsonElement>("/api/v1/notifications/delivery-log")).EnumerateArray());
        Assert.Equal("Pending", row.GetProperty("status").GetString());
        Assert.Equal("candidate.regret", row.GetProperty("templateKey").GetString());
        Assert.Equal("asha.rao@example.test", row.GetProperty("to").GetString());
    }

    [Fact]
    public async Task A_public_applicant_is_emailed_through_their_candidate_record_and_has_no_bell_item()
    {
        // career.job.applied.v1 (Careers): public applicants have no user account, only a candidateId.
        var tenant = Guid.NewGuid();
        var candidate = Guid.NewGuid();
        api.Backends.Candidates[candidate] = ("P. Nair", "p.nair@example.test");

        await api.PublishAsync(
            "career.job.applied.v1",
            new { appId = "APP-C1", jobId = "post-2026-0156", reqId = "REQ-1", candidateId = candidate, possibleDuplicate = false },
            tenant: tenant);
        await DispatchAsync();

        var sent = Assert.Single(api.Mail.Sent, m => m.TenantId == tenant);
        Assert.Equal("p.nair@example.test", sent.ToAddress);
        Assert.Equal("P. Nair", sent.ToName);
        Assert.Equal("We received your application (APP-C1)", sent.Subject);
        var bell = await api.ClientFor(tenant, RecuroRoles.Candidate, candidate.ToString()).GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        Assert.Equal(0, bell.GetArrayLength());
    }

    [Fact]
    public async Task An_unknown_candidate_or_a_masked_address_is_suppressed_not_sent()
    {
        var tenant = Guid.NewGuid();
        var masked = Guid.NewGuid();
        api.Backends.Candidates[masked] = ("A***", "a***@***");

        await api.PublishAsync("offer.chase_due.v1", new { offerId = "OFF-U", appId = "APP-U", candidateId = Guid.NewGuid(), chaseNumber = 1 }, tenant: tenant);
        await api.PublishAsync("offer.chase_due.v1", new { offerId = "OFF-M", appId = "APP-M", candidateId = masked, chaseNumber = 1 }, tenant: tenant);
        await DispatchAsync();

        Assert.DoesNotContain(api.Mail.Sent, m => m.TenantId == tenant);
        var log = await api.ClientFor(tenant, RecuroRoles.HrHead).GetFromJsonAsync<JsonElement>("/api/v1/notifications/delivery-log");
        Assert.All(log.EnumerateArray(), row => Assert.Equal("Suppressed", row.GetProperty("status").GetString()));
        Assert.Equal(2, log.GetArrayLength());
    }

    [Fact]
    public async Task Role_mail_goes_to_the_people_Identity_lists_and_they_are_remembered_for_when_Identity_is_down()
    {
        var tenant = Guid.NewGuid();
        api.Backends.Staff[(tenant, RecuroRoles.HrHead)] = [new StaffContact("head-7", "K. Menon", "k.menon@example.test")];

        await api.PublishAsync("bgv.adverse.flagged.v1", new { appId = "APP-I1", checkType = "Education" }, tenant: tenant);
        api.Backends.Staff.TryRemove((tenant, RecuroRoles.HrHead), out _);
        await api.PublishAsync("bgv.adverse.flagged.v1", new { appId = "APP-I2", checkType = "Education" }, tenant: tenant);
        await DispatchAsync();

        var sent = api.Mail.Sent.Where(m => m.TenantId == tenant).ToList();
        Assert.Equal(2, sent.Count);
        Assert.All(sent, m => Assert.Equal(("k.menon@example.test", "K. Menon"), (m.ToAddress, m.ToName)));
        Assert.Contains(api.Backends.Seen, s => s.Path == "/api/v1/identity/users" && s.Tenant == tenant.ToString());
    }

    [Fact]
    public async Task People_who_signed_in_are_emailed_at_the_address_on_their_token()
    {
        var tenant = Guid.NewGuid();
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ScopeContext>();
            context.SetTenant(tenant);
            context.SetUser("head-9", "R. Iyer", [RecuroRoles.HrHead]);
            await scope.ServiceProvider.GetRequiredService<ICommandHandler<RememberContactCommand>>()
                .Handle(new RememberContactCommand("R. Iyer", "r.iyer@example.test"), CancellationToken.None);
        }

        // Identity's events carry roles only; they must not wipe the address.
        await api.PublishAsync("identity.role.changed.v1", new { userId = "head-9", from = new[] { "hrhead" }, to = new[] { "hrhead", "mdceo" } }, tenant: tenant);
        await api.PublishAsync("vendor.sla.breached.v1", new { vendorId = "VND-1" }, tenant: tenant);
        await DispatchAsync();

        var sent = Assert.Single(api.Mail.Sent, m => m.TenantId == tenant);
        Assert.Equal("r.iyer@example.test", sent.ToAddress);
        Assert.Equal("R. Iyer", sent.ToName);
    }
}
