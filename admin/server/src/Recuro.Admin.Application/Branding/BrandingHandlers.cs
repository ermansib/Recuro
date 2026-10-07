using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Application.Themes;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Tenants;
using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.Application.Branding;

/// <summary>What a tenant admin sees on the Branding page: their current theme and the published presets to pick from.</summary>
public sealed record BrandingDto(string TenantName, string ThemePresetKey, ThemeMode ThemeMode, IReadOnlyList<ThemePresetDto> Presets);

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

        var published = await presets.ListAsync(publishedOnly: true, ct);
        return new BrandingDto(tenant.Name, tenant.ThemePresetKey, tenant.ThemeMode, published.Select(ThemePresetDto.From).ToList());
    }
}

public sealed record UpdateBrandingRequest(string ThemePresetKey, ThemeMode ThemeMode);

public sealed class UpdateBrandingHandler(
    ITenantContext tenantContext,
    ITenantRepository tenants,
    IThemePresetRepository presets,
    IUnitOfWork unitOfWork,
    GetBrandingHandler getBranding)
{
    public async Task<Result<BrandingDto>> HandleAsync(UpdateBrandingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

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

        var preset = await presets.GetByKeyAsync(request.ThemePresetKey, ct);
        if (preset is null)
        {
            return ThemeErrors.NotFound(request.ThemePresetKey);
        }

        var applied = tenant.ApplyTheme(preset, request.ThemeMode);
        if (applied.IsFailure)
        {
            return applied.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return await getBranding.HandleAsync(ct);
    }
}
