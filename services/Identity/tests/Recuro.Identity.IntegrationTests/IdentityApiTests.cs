using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Identity.Infrastructure.Persistence;

namespace Recuro.Identity.IntegrationTests;

public sealed class IdentityApiTests(IdentityApiFactory api) : IClassFixture<IdentityApiFactory>
{
    private static string NewSubject() => $"sub-{Guid.NewGuid():N}";

    private static object Decision(string actorId, string role, string action, bool mfa = false, params string[] assignees) => new
    {
        actor = new { id = actorId, roles = new[] { role } },
        action,
        resource = new { type = "Offer", id = "OFF-1", assigneeIds = assignees },
        context = new { mfa },
    };

    [Fact]
    public async Task Me_provisions_the_person_just_in_time_in_the_frontend_User_shape()
    {
        var subject = NewSubject();

        var me = await api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.HrTa, subject, "Riya Sharma")
            .GetFromJsonAsync<JsonElement>("/api/v1/identity/me");

        // Exactly the frontend User fields (frontend/src/domain/types.ts).
        Assert.Equal(
            ["email", "id", "initials", "name", "role", "summary", "tenantId", "title"],
            me.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(subject, me.GetProperty("id").GetString());
        Assert.Equal("hrta", me.GetProperty("role").GetString());
        Assert.Equal("RS", me.GetProperty("initials").GetString());
        Assert.Equal(1, await OutboxCountAsync(IdentityApiFactory.TenantA, "identity.user.provisioned.v1", subject));
    }

    [Fact]
    public async Task A_role_change_in_the_token_updates_the_mirror_and_publishes_one_event()
    {
        var subject = NewSubject();
        await api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.HrTa, subject).GetAsync("/api/v1/identity/me");
        await api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.HrTa, subject).GetAsync("/api/v1/identity/me");

        var me = await api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.HrHead, subject)
            .GetFromJsonAsync<JsonElement>("/api/v1/identity/me");

        Assert.Equal("hrhead", me.GetProperty("role").GetString());
        Assert.Equal(1, await OutboxCountAsync(IdentityApiFactory.TenantA, "identity.role.changed.v1", subject));
    }

    [Fact]
    public async Task Parallel_first_requests_provision_the_person_once()
    {
        var subject = NewSubject();
        var client = api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.HrTa, subject);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.GetAsync("/api/v1/identity/me")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, await OutboxCountAsync(IdentityApiFactory.TenantA, "identity.user.provisioned.v1", subject));
    }

    [Fact]
    public async Task The_team_list_is_per_tenant_and_filters_by_role()
    {
        var mine = NewSubject();
        var theirs = NewSubject();
        await api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.MdCeo, mine).GetAsync("/api/v1/identity/me");
        await api.ClientFor(IdentityApiFactory.TenantB, RecuroRoles.MdCeo, theirs).GetAsync("/api/v1/identity/me");

        var list = await api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.HrHead)
            .GetFromJsonAsync<JsonElement>("/api/v1/identity/users?role=mdceo");
        var ids = list.EnumerateArray().Select(u => u.GetProperty("id").GetString()).ToList();

        Assert.Contains(mine, ids);
        Assert.DoesNotContain(theirs, ids);
    }

    [Fact]
    public async Task Candidates_cannot_list_people()
    {
        var response = await api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.Candidate).GetAsync("/api/v1/identity/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_role_filter_is_a_400_with_field_errors()
    {
        var response = await api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.HrHead).GetAsync("/api/v1/identity/users?role=wizard");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("role", problem.GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task The_PDP_allows_per_the_matrix_and_denies_by_default()
    {
        var service = api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.Service, "svc-offer");

        var allowed = await (await service.PostAsJsonAsync("/api/v1/identity/decide", Decision("u-1", "hrta", "offer.edit")))
            .Content.ReadFromJsonAsync<JsonElement>();
        var denied = await (await service.PostAsJsonAsync("/api/v1/identity/decide", Decision("u-1", "mdceo", "pipeline.move")))
            .Content.ReadFromJsonAsync<JsonElement>();
        var unknown = await (await service.PostAsJsonAsync("/api/v1/identity/decide", Decision("u-1", "hrhead", "payroll.run")))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(allowed.GetProperty("allow").GetBoolean());
        Assert.True(allowed.GetProperty("ttlSeconds").GetInt32() <= 60);
        Assert.False(string.IsNullOrEmpty(allowed.GetProperty("policyVersion").GetString()));
        Assert.False(denied.GetProperty("allow").GetBoolean());
        Assert.Equal("role_not_permitted", denied.GetProperty("reasons")[0].GetString());
        Assert.Equal("unknown_action", unknown.GetProperty("reasons")[0].GetString());
    }

    [Fact]
    public async Task The_PDP_asks_elevated_roles_for_step_up_MFA()
    {
        var service = api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.Service, "svc-offer");

        var noMfa = await (await service.PostAsJsonAsync("/api/v1/identity/decide", Decision("u-9", "mdceo", "offer.approve")))
            .Content.ReadFromJsonAsync<JsonElement>();
        var mfa = await (await service.PostAsJsonAsync("/api/v1/identity/decide", Decision("u-9", "mdceo", "offer.approve", mfa: true)))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("step_up_required", noMfa.GetProperty("reasons")[0].GetString());
        Assert.True(mfa.GetProperty("allow").GetBoolean());
    }

    [Fact]
    public async Task A_decide_request_without_an_action_is_a_400()
    {
        var response = await api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.Service)
            .PostAsJsonAsync("/api/v1/identity/decide", new { actor = new { id = "u-1", roles = new[] { "hrta" } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_masking_map_hides_CTC_from_MD_CEO()
    {
        var map = await api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.Service)
            .GetFromJsonAsync<JsonElement>("/api/v1/identity/masking/mdceo/candidate");

        Assert.Equal("candidate", map.GetProperty("resource").GetString());
        Assert.Equal("hide", map.GetProperty("fields").GetProperty("currentCtc").GetString());
        Assert.Equal("partial", map.GetProperty("fields").GetProperty("phone").GetString());
    }

    [Fact]
    public async Task Service_accounts_have_a_masking_map()
    {
        var map = await api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.Service)
            .GetFromJsonAsync<JsonElement>("/api/v1/identity/masking/service/candidate");

        var fields = map.GetProperty("fields");
        Assert.False(fields.TryGetProperty("email", out _));
        Assert.Equal("hide", fields.GetProperty("currentCtc").GetString());
    }

    [Fact]
    public async Task An_unknown_masking_resource_is_a_404()
    {
        var response = await api.ClientFor(IdentityApiFactory.TenantA, RecuroRoles.Service).GetAsync("/api/v1/identity/masking/hrta/payslip");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var response = await api.CreateClient().GetAsync("/api/v1/identity/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<int> OutboxCountAsync(Guid tenant, string type, string subject)
    {
        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(tenant);
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var envelopes = await db.OutboxMessages.Where(m => m.Type == type).Select(m => m.Envelope).ToListAsync();
        return envelopes.Count(e => e.Contains(subject, StringComparison.Ordinal));
    }
}
