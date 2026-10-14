using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Recuro.Admin.Application.Roles;
using Recuro.Admin.Application.Workspaces;
using Recuro.Admin.Domain.Tenants;
using Recuro.Admin.Infrastructure.Persistence.Seed;

namespace Recuro.Admin.IntegrationTests;

public sealed class RoleApiTests : IDisposable
{
    private readonly AdminApiFactory _factory = new();
    private static readonly JsonSerializerOptions Json = AdminApiFactory.Json;

    public void Dispose() => _factory.Dispose();

    private static CreateRoleRequest Recruiter(string key = "recruiter", string? reportsTo = "hrta") =>
        new(key, "Recruiter", "Sources candidates for HR-TA", reportsTo, ["candidate.log", "pipeline.move"], ["dashboard", "pipeline"], RequiresMfa: false);

    [Fact]
    public async Task Existing_workspaces_get_the_default_roles()
    {
        var roles = await _factory.ClientAs(AdminApiFactory.Aurora).GetFromJsonAsync<List<RoleDto>>("/api/v1/workspace/roles", Json);

        Assert.Equal(["mdceo", "hrhead", "hod", "hrta", "employee"], roles!.Select(r => r.Key));
        Assert.Equal("hrhead", roles!.Single(r => r.Key == "hod").ReportsTo);
    }

    [Fact]
    public async Task A_tenant_admin_adds_a_role_that_only_their_workspace_sees()
    {
        var aurora = _factory.ClientAs(AdminApiFactory.Aurora);

        var created = await aurora.PostAsJsonAsync("/api/v1/workspace/roles", Recruiter(), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var duplicate = await aurora.PostAsJsonAsync("/api/v1/workspace/roles", Recruiter(), Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var auroraRoles = await aurora.GetFromJsonAsync<List<RoleDto>>("/api/v1/workspace/roles", Json);
        var bridgeRoles = await _factory.ClientAs(AdminApiFactory.TalentBridge).GetFromJsonAsync<List<RoleDto>>("/api/v1/workspace/roles", Json);
        Assert.Contains(auroraRoles!, r => r.Key == "recruiter" && !r.IsSystem);
        Assert.DoesNotContain(bridgeRoles!, r => r.Key == "recruiter");
    }

    [Theory]
    [InlineData("ghost", "role.reportsTo")]
    [InlineData(null, null)]
    public async Task The_reporting_line_must_point_at_an_existing_role(string? reportsTo, string? code)
    {
        var response = await _factory.ClientAs(AdminApiFactory.Aurora).PostAsJsonAsync("/api/v1/workspace/roles", Recruiter(reportsTo: reportsTo), Json);

        Assert.Equal(code is null ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Hierarchies_cannot_loop()
    {
        var aurora = _factory.ClientAs(AdminApiFactory.Aurora);

        // hrhead reports to mdceo; making mdceo report to hod (who reports to hrhead) would loop.
        var mdceo = new UpdateRoleRequest("MD/CEO", null, "hod", ["mrf.approve"], ["dashboard"], RequiresMfa: true);
        var response = await aurora.PutAsJsonAsync("/api/v1/workspace/roles/mdceo", mdceo, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("role.hierarchyCycle", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_pages_are_refused()
    {
        var request = Recruiter() with { Pages = ["payroll"] };

        var response = await _factory.ClientAs(AdminApiFactory.Aurora).PostAsJsonAsync("/api/v1/workspace/roles", request, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Requiring_mfa_on_a_role_updates_the_workspace_sign_in_rules()
    {
        var aurora = _factory.ClientAs(AdminApiFactory.Aurora);
        var hrta = new UpdateRoleRequest("Talent partner", null, "hrhead", ["mrf.raise"], ["dashboard", "mrf"], RequiresMfa: true);

        var response = await aurora.PutAsJsonAsync("/api/v1/workspace/roles/hrta", hrta, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var workspace = await aurora.GetFromJsonAsync<WorkspaceDto>("/api/v1/workspaces/aurora", Json);
        Assert.Equal(["mdceo", "hrhead", "hrta"], workspace!.MfaRoles);
    }

    [Fact]
    public async Task Roles_held_by_people_or_with_reports_stay_on()
    {
        var aurora = _factory.ClientAs(AdminApiFactory.Aurora);

        var withReports = await aurora.PutAsJsonAsync("/api/v1/workspace/roles/hrhead/status", new SetRoleStatusRequest(false), Json);
        Assert.Equal(HttpStatusCode.Conflict, withReports.StatusCode);

        var off = await aurora.PutAsJsonAsync("/api/v1/workspace/roles/employee/status", new SetRoleStatusRequest(false), Json);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.False((await off.Content.ReadFromJsonAsync<RoleDto>(Json))!.IsActive);
    }

    [Fact]
    public async Task Sign_up_makes_the_owner_the_first_member_and_roles_can_be_reassigned()
    {
        var anonymous = _factory.ClientAs(null);
        var signUp = await (await anonymous.PostAsJsonAsync(
                "/api/v1/workspaces",
                new SignUpWorkspaceRequest("Globex", OrgType.Enterprise, "Asha Rao", "hrhead", "asha@globex.example", "Str0ng!pass", true),
                Json))
            .Content.ReadFromJsonAsync<WorkspaceSignUpDto>(Json);
        var platform = _factory.ClientAs("platform");
        var basePath = $"/api/v1/platform/workspaces/{signUp!.Workspace.Id}";

        var members = await platform.GetFromJsonAsync<List<MemberDto>>($"{basePath}/members", Json);
        var owner = Assert.Single(members!);
        Assert.Equal(["hrhead"], owner.RoleKeys);

        await platform.PostAsJsonAsync($"{basePath}/roles", Recruiter(), Json);
        var update = await platform.PutAsJsonAsync($"{basePath}/members/{owner.AccountId}", new UpdateMemberRequest(["hrhead", "recruiter"], "Talent"), Json);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var roles = await platform.GetFromJsonAsync<List<RoleDto>>($"{basePath}/roles", Json);
        Assert.Equal(1, roles!.Single(r => r.Key == "recruiter").MemberCount);
        var inUse = await platform.PutAsJsonAsync($"{basePath}/roles/recruiter/status", new SetRoleStatusRequest(false), Json);
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
    }

    [Fact]
    public async Task Tenant_admins_cannot_reach_another_workspace_through_the_platform_routes()
    {
        var aurora = _factory.ClientAs(AdminApiFactory.Aurora);

        var response = await aurora.GetAsync(new Uri($"/api/v1/platform/workspaces/{DatabaseSeeder.TalentBridgeTenantId}/roles", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_catalogue_lists_permissions_and_screens()
    {
        var catalog = await _factory.ClientAs(AdminApiFactory.Aurora).GetFromJsonAsync<RoleCatalogDto>("/api/v1/workspace/roles/catalog", Json);

        Assert.Contains(catalog!.Permissions, p => p.Key == "offer.approve");
        Assert.Contains(catalog.Pages, p => p.Key == "pipeline" && p.Code == "S-06");
    }

    [Fact]
    public async Task Services_read_a_workspace_roles_and_members_and_people_cannot()
    {
        var service = _factory.CreateClient();
        service.DefaultRequestHeaders.Add("X-Dev-Roles", "service");
        var path = $"/api/v1/internal/workspaces/{DatabaseSeeder.AuroraTenantId}/access";

        var before = await service.GetFromJsonAsync<WorkspaceAccessDto>(path, Json);
        Assert.Contains(before!.Roles, r => r.Key == "hrta" && r.Permissions.Contains("mrf.raise"));

        await _factory.ClientAs(AdminApiFactory.Aurora).PostAsJsonAsync("/api/v1/workspace/roles", Recruiter(), Json);
        var after = await service.GetFromJsonAsync<WorkspaceAccessDto>(path, Json);
        Assert.Contains(after!.Roles, r => r.Key == "recruiter");
        Assert.NotEqual(before.Version, after.Version);

        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.ClientAs(AdminApiFactory.Aurora).GetAsync(new Uri(path, UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.ClientAs("platform").GetAsync(new Uri(path, UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await service.GetAsync(new Uri($"/api/v1/internal/workspaces/{Guid.NewGuid()}/access", UriKind.Relative))).StatusCode);
    }
}
