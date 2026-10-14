using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Identity.Application.Abstractions;
using Recuro.Identity.Domain.Access;

namespace Recuro.Identity.IntegrationTests;

/// <summary>The PDP and masking follow the roles a workspace configured in the admin portal.</summary>
public sealed class WorkspaceRolesTests(IdentityApiFactory api) : IClassFixture<IdentityApiFactory>
{
    private static readonly WorkspaceAccess AuroraRoles = new(
        "roles-abc",
        [
            new WorkspaceRoleDefinition("recruiter", ["candidate.log", "pipeline.move"], RequiresMfa: false, IsActive: true),
            new WorkspaceRoleDefinition("hrta", ["jd.edit"], RequiresMfa: false, IsActive: true),
        ],
        new Dictionary<string, IReadOnlyList<string>> { ["u-42"] = ["recruiter"] });

    private HttpClient ServiceFor(Guid tenant)
    {
        var factory = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IWorkspaceAccessSource>(_ => new StubWorkspaces(new Dictionary<Guid, WorkspaceAccess> { [IdentityApiFactory.TenantA] = AuroraRoles }))));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.UserHeader, "svc-pipeline");
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.RolesHeader, RecuroRoles.Service);
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.TenantHeader, tenant.ToString());
        return client;
    }

    private static async Task<JsonElement> DecideAsync(HttpClient client, string actorId, string role, string action) =>
        await (await client.PostAsJsonAsync("/api/v1/identity/decide", new
        {
            actor = new { id = actorId, roles = new[] { role } },
            action,
            resource = new { type = "Application", id = "APP-1", assigneeIds = Array.Empty<string>() },
            context = new { mfa = false },
        })).Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task A_workspace_defined_role_is_allowed_what_the_workspace_gave_it()
    {
        var aurora = ServiceFor(IdentityApiFactory.TenantA);

        var recruiter = await DecideAsync(aurora, "u-1", "recruiter", "pipeline.move");
        var assigned = await DecideAsync(aurora, "u-42", "employee", "candidate.log");
        var hrta = await DecideAsync(aurora, "u-1", "hrta", "mrf.raise");

        Assert.True(recruiter.GetProperty("allow").GetBoolean());
        Assert.Equal("rbac-2026.10+roles-abc", recruiter.GetProperty("policyVersion").GetString());
        Assert.True(assigned.GetProperty("allow").GetBoolean());
        Assert.False(hrta.GetProperty("allow").GetBoolean());
    }

    [Fact]
    public async Task Another_workspace_keeps_its_own_roles()
    {
        var bridge = ServiceFor(IdentityApiFactory.TenantB);

        var recruiter = await DecideAsync(bridge, "u-1", "recruiter", "pipeline.move");
        var hrta = await DecideAsync(bridge, "u-1", "hrta", "mrf.raise");

        Assert.False(recruiter.GetProperty("allow").GetBoolean());
        Assert.True(hrta.GetProperty("allow").GetBoolean());
    }

    [Fact]
    public async Task Workspace_roles_get_a_masking_map()
    {
        var map = await ServiceFor(IdentityApiFactory.TenantA).GetFromJsonAsync<JsonElement>("/api/v1/identity/masking/recruiter/candidate");

        // No candidate.viewSensitive, so a recruiter is masked like MD/CEO.
        Assert.Equal("masking-2026.10.4+roles-abc", map.GetProperty("version").GetString());
        Assert.Equal("hide", map.GetProperty("fields").GetProperty("currentCtc").GetString());
    }

    private sealed class StubWorkspaces(Dictionary<Guid, WorkspaceAccess> workspaces) : IWorkspaceAccessSource
    {
        public Task<WorkspaceAccess?> GetAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(workspaces.GetValueOrDefault(tenantId));
    }
}
