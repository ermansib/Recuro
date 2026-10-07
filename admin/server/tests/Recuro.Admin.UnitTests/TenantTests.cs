using Recuro.Admin.Domain.Tenants;
using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.UnitTests;

public class TenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalises_slug_and_starts_active_with_default_theme()
    {
        var result = Tenant.Create("  Acme Corp ", "Acme-Corp", TenantKind.InHouse, TenantPlan.Starter, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("Acme Corp", result.Value.Name);
        Assert.Equal("acme-corp", result.Value.Slug);
        Assert.Equal(TenantStatus.Active, result.Value.Status);
        Assert.Equal(ThemePreset.DefaultKey, result.Value.ThemePresetKey);
        Assert.Equal(ThemeMode.System, result.Value.ThemeMode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("acme corp")]
    [InlineData("acme_corp")]
    public void Create_rejects_invalid_slugs(string slug)
    {
        var result = Tenant.Create("Acme", slug, TenantKind.Agency, TenantPlan.Starter, Now);

        Assert.Equal(TenantErrors.InvalidSlug, result.Error);
    }

    [Fact]
    public void Suspend_twice_is_a_conflict()
    {
        var tenant = Tenant.Create("Acme", "acme", TenantKind.Agency, TenantPlan.Starter, Now).Value;

        Assert.True(tenant.Suspend().IsSuccess);
        Assert.Equal("tenant.status", tenant.Suspend().Error!.Code);
    }

    [Fact]
    public void SetCustomDomain_rejects_non_host_names()
    {
        var tenant = Tenant.Create("Acme", "acme", TenantKind.Agency, TenantPlan.Starter, Now).Value;

        Assert.Equal(TenantErrors.InvalidDomain, tenant.SetCustomDomain("not a domain").Error);
        Assert.True(tenant.SetCustomDomain("Careers.Acme.com").IsSuccess);
        Assert.Equal("careers.acme.com", tenant.CustomDomain);
    }

    [Fact]
    public void ApplyTheme_requires_a_published_preset()
    {
        var tenant = Tenant.Create("Acme", "acme", TenantKind.Agency, TenantPlan.Starter, Now).Value;
        var preset = ThemePresetTests.ValidPreset();

        Assert.Equal("theme.notPublished", tenant.ApplyTheme(preset, ThemeMode.Dark).Error!.Code);

        preset.Publish();
        Assert.True(tenant.ApplyTheme(preset, ThemeMode.Dark).IsSuccess);
        Assert.Equal(preset.Key, tenant.ThemePresetKey);
        Assert.Equal(ThemeMode.Dark, tenant.ThemeMode);
    }
}
