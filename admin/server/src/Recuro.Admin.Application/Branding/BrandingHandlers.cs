using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Application.Themes;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Tenants;
using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.Application.Branding;

/// <summary>
/// What a tenant admin sees on the Branding page: the theme the platform console assigned to them.
/// Tenants can't change it themselves; see <see cref="Tenants.AssignTenantThemeHandler"/>.
/// </summary>
public sealed record BrandingDto(string TenantName, string ThemePresetKey, ThemeMode ThemeMode, ThemePresetDto Theme);

public sealed class GetBrandingHandler(ITenantContext tenantContext, ITenantRepository tenants, IThemePresetRepository presets)
{
    public async Task<Result<BrandingDto>> HandleAsync(CancellationToken ct)
    {
        var tenantId = TenantScope.Require(tenantContext);
        if (tenantId.IsFailure)
        {
            return tenantId.Error!;
        }

        var tenant = await tenants.GetByIdAsync(tenantId.Value, ct);
        if (tenant is null)
        {
            return TenantErrors.NotFound(tenantId.Value);
        }

        // An assigned preset stays in effect even if it is later withdrawn from the library.
        var preset = await presets.GetByKeyAsync(tenant.ThemePresetKey, ct)
            ?? await presets.GetByKeyAsync(ThemePreset.DefaultKey, ct);
        if (preset is null)
        {
            return ThemeErrors.NotFound(tenant.ThemePresetKey);
        }

        return new BrandingDto(tenant.Name, preset.Key, tenant.ThemeMode, ThemePresetDto.From(preset));
    }
}
