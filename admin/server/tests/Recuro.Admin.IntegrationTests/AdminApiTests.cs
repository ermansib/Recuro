using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Recuro.Admin.Application.Branding;
using Recuro.Admin.Application.Runtime;
using Recuro.Admin.Application.Screens;
using Recuro.Admin.Application.Tenants;
using Recuro.Admin.Application.Themes;
using Recuro.Admin.Domain.Screens;
using Recuro.Admin.Domain.Tenants;
using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.IntegrationTests;

public sealed class AdminApiTests : IDisposable
{
    private readonly AdminApiFactory _factory = new();
    private static readonly JsonSerializerOptions Json = AdminApiFactory.Json;

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Anonymous_callers_cannot_reach_admin_endpoints()
    {
        var client = _factory.ClientAs(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/v1/platform/tenants", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/v1/tenant/screens", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Tenant_admins_cannot_use_the_platform_console()
    {
        var client = _factory.ClientAs(AdminApiFactory.Aurora);

        var response = await client.GetAsync(new Uri("/api/v1/platform/tenants", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Platform_admin_lists_and_creates_tenants_with_unique_slugs()
    {
        var client = _factory.ClientAs("platform");

        var tenants = await client.GetFromJsonAsync<List<TenantDto>>("/api/v1/platform/tenants", Json);
        Assert.Equal(["Aurora Housing Finance", "TalentBridge Staffing"], tenants!.Select(t => t.Name));

        var request = new CreateTenantRequest("Acme Corp", "acme", TenantKind.InHouse, TenantPlan.Starter, "careers.acme.example");
        var created = await client.PostAsJsonAsync("/api/v1/platform/tenants", request, Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var duplicate = await client.PostAsJsonAsync("/api/v1/platform/tenants", request, Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task All_six_seeded_themes_are_published()
    {
        var client = _factory.ClientAs("platform");

        var themes = await client.GetFromJsonAsync<List<ThemePresetDto>>("/api/v1/platform/themes", Json);

        Assert.Equal(6, themes!.Count);
        Assert.All(themes, t => Assert.True(t.IsPublished));
    }

    [Fact]
    public async Task Screen_overrides_are_isolated_per_tenant()
    {
        var aurora = _factory.ClientAs(AdminApiFactory.Aurora);
        var talentBridge = _factory.ClientAs(AdminApiFactory.TalentBridge);

        var update = new UpdateScreenRequest(true, "Raise a hiring request", null, [new UpdateFieldRequest("location", "Branch", true, true, 40)]);
        var response = await aurora.PutAsJsonAsync("/api/v1/tenant/screens/mrf", update, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auroraScreen = await aurora.GetFromJsonAsync<EffectiveScreen>("/api/v1/tenant/screens/mrf", Json);
        var bridgeScreen = await talentBridge.GetFromJsonAsync<EffectiveScreen>("/api/v1/tenant/screens/mrf", Json);

        Assert.Equal("Raise a hiring request", auroraScreen!.Title);
        Assert.Equal("Branch", auroraScreen.Fields.Single(f => f.Key == "location").Label);
        Assert.Equal("New manpower requisition", bridgeScreen!.Title);
        Assert.Equal("Location", bridgeScreen.Fields.Single(f => f.Key == "location").Label);
    }

    [Fact]
    public async Task Locked_fields_cannot_be_hidden_through_the_api()
    {
        var client = _factory.ClientAs(AdminApiFactory.Aurora);
        var update = new UpdateScreenRequest(true, null, null, [new UpdateFieldRequest("grade", null, false, false, 1)]);

        var response = await client.PutAsJsonAsync("/api/v1/tenant/screens/mrf", update, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("screen.lockedField", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Branding_change_reaches_the_runtime_config_for_that_tenant_only()
    {
        var aurora = _factory.ClientAs(AdminApiFactory.Aurora);
        var anonymous = _factory.ClientAs(null);

        var response = await aurora.PutAsJsonAsync("/api/v1/tenant/branding", new UpdateBrandingRequest("royal-plum", ThemeMode.Dark), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var branding = await response.Content.ReadFromJsonAsync<BrandingDto>(Json);
        Assert.Equal("royal-plum", branding!.ThemePresetKey);

        var auroraRuntime = await anonymous.GetFromJsonAsync<RuntimeConfigDto>("/api/v1/runtime/aurora", Json);
        var bridgeRuntime = await anonymous.GetFromJsonAsync<RuntimeConfigDto>("/api/v1/runtime/talentbridge", Json);

        Assert.Equal(ThemeMode.Dark, auroraRuntime!.ThemeMode);
        Assert.Equal("#4C1D95", auroraRuntime.LightTheme["navy"]);
        Assert.Equal("#C4B5FD", auroraRuntime.DarkTheme["navy"]);
        Assert.Equal(ThemePreset.DefaultKey, bridgeRuntime!.ThemePresetKey);
        Assert.Equal(13, auroraRuntime.Screens.Count);
    }

    [Fact]
    public async Task Unknown_theme_is_not_found()
    {
        var client = _factory.ClientAs(AdminApiFactory.Aurora);

        var response = await client.PutAsJsonAsync("/api/v1/tenant/branding", new UpdateBrandingRequest("neon", ThemeMode.Light), Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Suspended_tenants_get_no_runtime_config()
    {
        var platform = _factory.ClientAs("platform");
        var anonymous = _factory.ClientAs(null);

        var response = await platform.PutAsJsonAsync(
            $"/api/v1/platform/tenants/{Infrastructure.Persistence.Seed.DatabaseSeeder.TalentBridgeTenantId}/status",
            new ChangeTenantStatusRequest(TenantStatus.Suspended),
            Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var runtime = await anonymous.GetAsync(new Uri("/api/v1/runtime/talentbridge", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, runtime.StatusCode);
    }
}
