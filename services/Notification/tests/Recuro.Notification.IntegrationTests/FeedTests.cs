using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Notification.IntegrationTests;

[Collection(NotificationCollection.Name)]
public sealed class FeedTests(NotificationApiFactory api)
{
    private static string NewReq() => $"REQ-{Guid.NewGuid():N}"[..20];

    private static async Task<JsonElement[]> BellAsync(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/notifications{query}")).EnumerateArray().ToArray();

    private static JsonElement[] About(IEnumerable<JsonElement> items, string text) =>
        items.Where(i => i.GetProperty("title").GetString()!.Contains(text, StringComparison.Ordinal)).ToArray();

    [Fact]
    public async Task A_matrix_event_reaches_the_role_in_the_frontend_AppNotification_shape()
    {
        var req = NewReq();
        await api.PublishAsync("recruitment.mrf.cancelled.v1", new { reqId = req, reason = "Budget freeze" }, subject: $"Requisition/{req}");

        var item = Assert.Single(About(await BellAsync(api.ClientFor(NotificationApiFactory.TenantA, RecuroRoles.HrTa)), req));

        Assert.Equal(
            ["body", "createdAt", "icon", "id", "link", "recipientRole", "title", "unread"],
            item.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("hrta", item.GetProperty("recipientRole").GetString());
        Assert.Equal($"MRF cancelled — {req}", item.GetProperty("title").GetString());
        Assert.Equal("Reason: Budget freeze", item.GetProperty("body").GetString());
        Assert.True(item.GetProperty("unread").GetBoolean());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", item.GetProperty("createdAt").GetString());
    }

    [Fact]
    public async Task Other_roles_and_other_tenants_never_see_it()
    {
        var req = NewReq();
        await api.PublishAsync("recruitment.mrf.cancelled.v1", new { reqId = req }, subject: $"Requisition/{req}");

        Assert.Empty(About(await BellAsync(api.ClientFor(NotificationApiFactory.TenantA, RecuroRoles.HrHead)), req));
        Assert.Empty(About(await BellAsync(api.ClientFor(NotificationApiFactory.TenantA, RecuroRoles.Candidate)), req));
        Assert.Empty(About(await BellAsync(api.ClientFor(NotificationApiFactory.TenantB, RecuroRoles.HrTa)), req));
    }

    [Fact]
    public async Task A_redelivered_event_notifies_once()
    {
        var req = NewReq();
        var id = Guid.CreateVersion7();

        await api.PublishAsync("recruitment.mrf.cancelled.v1", new { reqId = req }, id: id);
        await api.PublishAsync("recruitment.mrf.cancelled.v1", new { reqId = req }, id: id);

        Assert.Single(About(await BellAsync(api.ClientFor(NotificationApiFactory.TenantA, RecuroRoles.HrTa)), req));
    }

    [Fact]
    public async Task The_initiator_hears_that_their_MRF_was_approved()
    {
        var req = NewReq();
        var subject = $"Requisition/{req}";
        await api.PublishAsync("recruitment.mrf.submitted.v1", new { reqId = req }, subject: subject, actorId: "initiator-1", actorName: "A. Sharma");

        await api.PublishAsync("recruitment.mrf.approved.v1", new { reqId = req, decision = "approve" }, subject: subject, actorId: "approver-9");

        Assert.Single(About(await BellAsync(api.ClientFor(NotificationApiFactory.TenantA, RecuroRoles.HrTa, "initiator-1")), req));
        Assert.Empty(About(await BellAsync(api.ClientFor(NotificationApiFactory.TenantA, RecuroRoles.HrTa, "someone-else")), req));
    }

    [Fact]
    public async Task An_applicant_alone_gets_their_confirmation()
    {
        var app = $"APP-{Guid.NewGuid():N}"[..16];
        await api.PublishAsync("career.job.applied.v1", new { appId = app, jobId = "JOB-1" }, actorId: "cand-1", actorName: "P. Nair");

        var mine = await BellAsync(api.ClientFor(NotificationApiFactory.TenantA, RecuroRoles.Candidate, "cand-1"));
        var theirs = await BellAsync(api.ClientFor(NotificationApiFactory.TenantA, RecuroRoles.Candidate, "cand-2"));

        Assert.Single(mine, i => i.GetProperty("body").GetString()!.Contains(app, StringComparison.Ordinal));
        Assert.DoesNotContain(theirs, i => i.GetProperty("body").GetString()!.Contains(app, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reading_one_item_is_per_person_and_mark_all_clears_the_rest()
    {
        var tenant = Guid.NewGuid();
        await api.PublishAsync("bgv.cleared.v1", new { appId = "APP-1" }, tenant: tenant);
        await api.PublishAsync("bgv.cleared.v1", new { appId = "APP-2" }, tenant: tenant);
        var me = api.ClientFor(tenant, RecuroRoles.HrTa, "u-me");
        var colleague = api.ClientFor(tenant, RecuroRoles.HrTa, "u-colleague");
        var first = (await BellAsync(me))[0].GetProperty("id").GetString();

        var read = await me.PostAsJsonAsync("/api/v1/notifications/read", new { ids = new[] { first } });

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Single(await BellAsync(me, "?filter=unread"));
        Assert.Equal(2, (await BellAsync(colleague, "?filter=unread")).Length);
        Assert.Equal(1, (await me.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count")).GetProperty("notifications").GetInt32());

        await me.PostAsJsonAsync("/api/v1/notifications/read", new { all = true });

        Assert.Empty(await BellAsync(me, "?filter=unread"));
        Assert.Equal(0, (await me.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count")).GetProperty("notifications").GetInt32());
    }

    [Fact]
    public async Task Paging_with_before_returns_older_items()
    {
        var tenant = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            await api.PublishAsync("bgv.cleared.v1", new { appId = $"APP-{i}" }, tenant: tenant);
        }

        var client = api.ClientFor(tenant, RecuroRoles.HrTa);
        var page1 = await BellAsync(client, "?limit=2");
        var page2 = await BellAsync(client, $"?limit=2&before={page1[^1].GetProperty("id").GetString()}");

        Assert.Equal(["BGV cleared — APP-2", "BGV cleared — APP-1"], page1.Select(i => i.GetProperty("title").GetString()));
        Assert.Equal("BGV cleared — APP-0", Assert.Single(page2).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Marking_someone_elses_item_changes_nothing()
    {
        var tenant = Guid.NewGuid();
        await api.PublishAsync("career.job.applied.v1", new { appId = "APP-9" }, tenant: tenant, actorId: "cand-1");
        var item = (await BellAsync(api.ClientFor(tenant, RecuroRoles.Candidate, "cand-1")))[0].GetProperty("id").GetString();

        var response = await api.ClientFor(tenant, RecuroRoles.Candidate, "cand-2").PostAsJsonAsync("/api/v1/notifications/read", new { ids = new[] { item } });

        Assert.Equal(0, await response.Content.ReadFromJsonAsync<int>());
        Assert.Single(await BellAsync(api.ClientFor(tenant, RecuroRoles.Candidate, "cand-1"), "?filter=unread"));
    }

    [Fact]
    public async Task Bad_input_returns_400_with_field_errors()
    {
        var client = api.ClientFor(NotificationApiFactory.TenantA, RecuroRoles.HrTa);

        var badFilter = await client.GetAsync("/api/v1/notifications?filter=starred");
        var noIds = await client.PostAsJsonAsync("/api/v1/notifications/read", new { });

        Assert.Equal(HttpStatusCode.BadRequest, badFilter.StatusCode);
        Assert.Equal("filter", (await badFilter.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, noIds.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_get_401_and_service_accounts_have_no_inbox()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/v1/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(NotificationApiFactory.TenantA, RecuroRoles.Service).GetAsync("/api/v1/notifications")).StatusCode);
    }
}

[CollectionDefinition(Name)]
public sealed class NotificationCollection : ICollectionFixture<NotificationApiFactory>
{
    public const string Name = "notification-api";
}
