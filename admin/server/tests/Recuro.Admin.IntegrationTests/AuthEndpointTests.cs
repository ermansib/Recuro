using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Recuro.Admin.Application.Tenants;
using Recuro.Admin.Domain.Tenants;
using Recuro.Admin.Infrastructure.Persistence.Seed;

namespace Recuro.Admin.IntegrationTests;

public sealed class AuthEndpointTests : IDisposable
{
    private readonly AdminApiFactory _factory = new();
    private static readonly JsonSerializerOptions Json = AdminApiFactory.Json;

    public void Dispose() => _factory.Dispose();

    private sealed record AuthConfig(string Mode, string? Authority, string? ClientId);

    private sealed record CurrentAdmin(string Name, string Level, Guid? TenantId, string? TenantName);

    [Fact]
    public async Task Config_tells_the_client_which_sign_in_to_use()
    {
        var config = await _factory.ClientAs(null).GetFromJsonAsync<AuthConfig>("/api/v1/auth/config", Json);

        Assert.Equal("development", config!.Mode);
    }

    [Fact]
    public async Task Me_requires_a_signed_in_user()
    {
        var response = await _factory.ClientAs(null).GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_reports_the_admin_level_and_tenant()
    {
        var platform = await _factory.ClientAs("platform").GetFromJsonAsync<CurrentAdmin>("/api/v1/auth/me", Json);
        var tenant = await _factory.ClientAs(AdminApiFactory.Aurora).GetFromJsonAsync<CurrentAdmin>("/api/v1/auth/me", Json);

        Assert.Equal("platform", platform!.Level);
        Assert.Null(platform.TenantId);
        Assert.Equal("tenant", tenant!.Level);
        Assert.Equal(DatabaseSeeder.AuroraTenantId, tenant.TenantId);
        Assert.Equal("Aurora Housing Finance", tenant.TenantName);
    }

    [Fact]
    public async Task Admins_of_a_suspended_tenant_are_refused()
    {
        await _factory.ClientAs("platform").PutAsJsonAsync(
            $"/api/v1/platform/tenants/{DatabaseSeeder.AuroraTenantId}/status",
            new ChangeTenantStatusRequest(TenantStatus.Suspended),
            Json);

        var aurora = _factory.ClientAs(AdminApiFactory.Aurora);
        var me = await aurora.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));
        var screens = await aurora.GetAsync(new Uri("/api/v1/tenant/screens", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, screens.StatusCode);
    }
}
