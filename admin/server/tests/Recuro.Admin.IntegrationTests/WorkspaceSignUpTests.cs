using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Application.Tenants;
using Recuro.Admin.Application.Workspaces;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.IntegrationTests;

public sealed class WorkspaceSignUpTests : IDisposable
{
    private readonly AdminApiFactory _factory = new();
    private static readonly JsonSerializerOptions Json = AdminApiFactory.Json;

    public void Dispose() => _factory.Dispose();

    private static SignUpWorkspaceRequest Request(string orgName = "Acme Corp", string password = "Str0ng!pass", string email = "Owner@Acme.example") =>
        new(orgName, OrgType.Agency, "Asha Rao", "hrhead", email, password, AcceptTerms: true);

    [Fact]
    public async Task Sign_up_saves_the_workspace_in_the_tenants_table()
    {
        var client = _factory.ClientAs(null);

        var response = await client.PostAsJsonAsync("/api/v1/workspaces", Request(), Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var signUp = await response.Content.ReadFromJsonAsync<WorkspaceSignUpDto>(Json);
        Assert.Equal("acme-corp", signUp!.Workspace.Slug);
        Assert.Equal(OrgType.Agency, signUp.Workspace.OrgType);
        Assert.Equal("acme.example", signUp.Workspace.EmailDomain);
        Assert.Equal(["hrhead", "mdceo"], signUp.Workspace.MfaRoles);
        Assert.Equal(new WorkspaceOwnerDto(signUp.Owner.Id, "Asha Rao", "owner@acme.example", "hrhead"), signUp.Owner);

        var found = await client.GetFromJsonAsync<WorkspaceDto>("/api/v1/workspaces/acme-corp", Json);
        Assert.Equal(JsonSerializer.Serialize(signUp.Workspace, Json), JsonSerializer.Serialize(found, Json));

        var tenants = await _factory.ClientAs("platform").GetFromJsonAsync<List<TenantDto>>("/api/v1/platform/workspaces", Json);
        var tenant = Assert.Single(tenants!, t => t.Slug == "acme-corp");
        Assert.Equal((TenantKind.Agency, TenantPlan.Starter, TenantStatus.Active), (tenant.Kind, tenant.Plan, tenant.Status));
    }

    [Fact]
    public async Task A_second_workspace_with_the_same_name_gets_its_own_slug()
    {
        var client = _factory.ClientAs(null);

        await client.PostAsJsonAsync("/api/v1/workspaces", Request(), Json);
        var second = await client.PostAsJsonAsync("/api/v1/workspaces", Request(email: "other@acme.example"), Json);

        var signUp = await second.Content.ReadFromJsonAsync<WorkspaceSignUpDto>(Json);
        Assert.Equal("acme-corp-2", signUp!.Workspace.Slug);
    }

    [Theory]
    [InlineData("weak", "owner@acme.example")]
    [InlineData("Str0ng!pass", "not-an-email")]
    public async Task Invalid_sign_ups_are_rejected_and_nothing_is_saved(string password, string email)
    {
        var client = _factory.ClientAs(null);

        var response = await client.PostAsJsonAsync("/api/v1/workspaces", Request(password: password, email: email), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri("/api/v1/workspaces/acme-corp", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task An_email_that_already_has_an_account_is_a_conflict_and_no_workspace_is_saved()
    {
        var client = WithDirectory(new RefusingDirectory(WorkspaceErrors.EmailTaken));

        var response = await client.PostAsJsonAsync("/api/v1/workspaces", Request(), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri("/api/v1/workspaces/acme-corp", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Keycloak_being_down_is_a_503_and_no_workspace_is_saved()
    {
        var client = WithDirectory(new RefusingDirectory(WorkspaceErrors.AccountServiceUnavailable));

        var response = await client.PostAsJsonAsync("/api/v1/workspaces", Request(), Json);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri("/api/v1/workspaces/acme-corp", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Suspended_workspaces_are_not_found()
    {
        var platform = _factory.ClientAs("platform");
        var tenants = await platform.GetFromJsonAsync<List<TenantDto>>("/api/v1/platform/workspaces", Json);
        var aurora = tenants!.Single(t => t.Slug == "aurora");
        Assert.Equal(HttpStatusCode.OK, (await platform.GetAsync(new Uri("/api/v1/workspaces/aurora", UriKind.Relative))).StatusCode);

        await platform.PutAsJsonAsync($"/api/v1/platform/workspaces/{aurora.Id}/status", new ChangeTenantStatusRequest(TenantStatus.Suspended), Json);

        Assert.Equal(HttpStatusCode.NotFound, (await platform.GetAsync(new Uri("/api/v1/workspaces/aurora", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task The_platform_console_creates_the_same_workspace_and_owner_with_a_temporary_password()
    {
        var directory = new RecordingDirectory();
        var platform = WithDirectory(directory, persona: "platform");

        var request = new CreateTenantRequest(
            "Acme Corp", OrgType.Agency, "Asha Rao", "hrhead", "Owner@Acme.example", "Str0ng!pass", TenantPlan.Professional, "careers.acme.example");
        var response = await platform.PostAsJsonAsync("/api/v1/platform/workspaces", request, Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var tenant = await response.Content.ReadFromJsonAsync<TenantDto>(Json);
        Assert.Equal(("acme-corp", OrgType.Agency, TenantKind.Agency, TenantPlan.Professional), (tenant!.Slug, tenant.OrgType, tenant.Kind, tenant.Plan));
        Assert.Equal(("acme.example", "careers.acme.example"), (tenant.EmailDomain, tenant.CustomDomain));
        var owner = Assert.Single(directory.Owners);
        Assert.Equal((tenant.Id, "Asha Rao", "owner@acme.example", "hrhead", true), (owner.TenantId, owner.Name, owner.Email, owner.Role, owner.MustChangePassword));

        var found = await platform.GetFromJsonAsync<WorkspaceDto>("/api/v1/workspaces/acme-corp", Json);
        Assert.Equal(["hrhead", "mdceo"], found!.MfaRoles);
    }

    [Theory]
    [InlineData("", "Asha Rao")]
    [InlineData("Acme Corp", "")]
    public async Task The_platform_console_applies_the_sign_up_rules(string orgName, string adminName)
    {
        var directory = new RecordingDirectory();
        var platform = WithDirectory(directory, persona: "platform");

        var request = new CreateTenantRequest(orgName, OrgType.SmallBusiness, adminName, "hrta", "owner@acme.example", "Str0ng!pass", TenantPlan.Starter, null);
        var response = await platform.PostAsJsonAsync("/api/v1/platform/workspaces", request, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(directory.Owners);
    }

    [Fact]
    public async Task Sign_up_owners_keep_the_password_they_chose()
    {
        var directory = new RecordingDirectory();

        var response = await WithDirectory(directory).PostAsJsonAsync("/api/v1/workspaces", Request(), Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(Assert.Single(directory.Owners).MustChangePassword);
    }

    private HttpClient WithDirectory(IAccountDirectory directory, string? persona = null)
    {
        var client = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddScoped(_ => directory)))
            .CreateClient();
        if (persona is not null)
        {
            client.DefaultRequestHeaders.Add("X-Recuro-Persona", persona);
        }

        return client;
    }

    private sealed class RecordingDirectory : IAccountDirectory
    {
        public List<NewWorkspaceOwner> Owners { get; } = [];

        public Task<Result<string>> CreateWorkspaceOwnerAsync(NewWorkspaceOwner owner, CancellationToken ct)
        {
            Owners.Add(owner);
            return Task.FromResult(Result.Success($"kc-{Owners.Count}"));
        }

        public Task DeleteAccountAsync(string accountId, CancellationToken ct) => Task.CompletedTask;

        public Task<Result<IReadOnlyList<DirectoryAccount>>> ListTenantAccountsAsync(
            Guid tenantId, IReadOnlySet<string> knownAccountIds, CancellationToken ct) =>
            Task.FromResult(Result.Success<IReadOnlyList<DirectoryAccount>>([]));

        public Task<Result> SyncWorkspaceRolesAsync(
            string accountId, IReadOnlyList<string> roleKeys, IReadOnlyCollection<string> workspaceRoleKeys, CancellationToken ct) =>
            Task.FromResult(Result.Success());
    }

    private sealed class RefusingDirectory(Error error) : IAccountDirectory
    {
        public Task<Result<string>> CreateWorkspaceOwnerAsync(NewWorkspaceOwner owner, CancellationToken ct) =>
            Task.FromResult(Result.Failure<string>(error));

        public Task DeleteAccountAsync(string accountId, CancellationToken ct) => Task.CompletedTask;

        public Task<Result<IReadOnlyList<DirectoryAccount>>> ListTenantAccountsAsync(
            Guid tenantId, IReadOnlySet<string> knownAccountIds, CancellationToken ct) =>
            Task.FromResult(Result.Failure<IReadOnlyList<DirectoryAccount>>(error));

        public Task<Result> SyncWorkspaceRolesAsync(
            string accountId, IReadOnlyList<string> roleKeys, IReadOnlyCollection<string> workspaceRoleKeys, CancellationToken ct) =>
            Task.FromResult(Result.Failure(error));
    }
}
