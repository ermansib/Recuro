using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Application.Screens;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Screens;
using Recuro.Admin.Domain.Tenants;
using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.Application.Runtime;

/// <summary>
/// Everything the main Recuro portal needs to render a tenant: name, theme tokens and screen layout.
/// Theme tokens are keyed by the main app's CSS custom properties (frontend/src/styles/tokens.css).
/// </summary>
public sealed record RuntimeConfigDto(
    string TenantName,
    string Slug,
    TenantKind Kind,
    string ThemePresetKey,
    ThemeMode ThemeMode,
    IReadOnlyDictionary<string, string> LightTheme,
    IReadOnlyDictionary<string, string> DarkTheme,
    IReadOnlyList<EffectiveScreen> Screens);

public static class ThemeTokens
{
    /// <summary>Maps a palette onto the main app's design tokens.</summary>
    public static IReadOnlyDictionary<string, string> From(ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["navy"] = palette.Primary,
            ["cy"] = palette.Secondary,
            ["gold"] = palette.Accent,
            ["bg"] = palette.Background,
            ["card"] = palette.Surface,
            ["txt"] = palette.Text,
        };
    }
}

public static class RuntimeErrors
{
    public static Error Suspended(string slug) =>
        Error.Forbidden("tenant.suspended", $"Tenant '{slug}' is suspended.");
}

public sealed class GetRuntimeConfigHandler(
    ITenantRepository tenants,
    IThemePresetRepository presets,
    IScreenCatalog catalog,
    IScreenConfigurationRepository configurations)
{
    public async Task<Result<RuntimeConfigDto>> HandleAsync(string slug, CancellationToken ct)
    {
        var tenant = await tenants.GetBySlugAsync(slug, ct);
        if (tenant is null)
        {
            return TenantErrors.NotFound(slug);
        }

        if (tenant.Status == TenantStatus.Suspended)
        {
            return RuntimeErrors.Suspended(slug);
        }

        // An unpublished preset stays in effect for tenants already using it, so look it up regardless of status.
        var preset = await presets.GetByKeyAsync(tenant.ThemePresetKey, ct)
            ?? await presets.GetByKeyAsync(ThemePreset.DefaultKey, ct);
        if (preset is null)
        {
            return ThemeErrors.NotFound(tenant.ThemePresetKey);
        }

        var screens = await ListScreensHandler.ResolveAllAsync(catalog, configurations, tenant.Id, ct);
        return new RuntimeConfigDto(
            tenant.Name,
            tenant.Slug,
            tenant.Kind,
            preset.Key,
            tenant.ThemeMode,
            ThemeTokens.From(preset.Light),
            ThemeTokens.From(preset.Dark),
            screens);
    }
}
