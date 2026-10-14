using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Application.Screens;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Screens;
using Recuro.Admin.Domain.Tenants;
using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.Application.Runtime;

/// <summary>
/// Everything the main Recuro portal needs to render a tenant: name, theme tokens, screen layout
/// and the workspace's active roles.
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
    IReadOnlyList<EffectiveScreen> Screens,
    IReadOnlyList<RuntimeRoleDto> Roles);

/// <summary>An active workspace role as the main portal needs it: what it may do and which screens it opens.</summary>
public sealed record RuntimeRoleDto(
    string Key,
    string Name,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Pages);

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
        Error.Forbidden("tenant.suspended", $"Workspace '{slug}' is suspended.");
}

public sealed class GetRuntimeConfigHandler(
    ITenantRepository tenants,
    IThemePresetRepository presets,
    IScreenCatalog catalog,
    IScreenConfigurationRepository configurations,
    IWorkspaceRoleRepository roles)
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
        var activeRoles = (await roles.ListAsync(tenant.Id, ct))
            .Where(r => r.IsActive)
            .Select(r => new RuntimeRoleDto(r.Key, r.Name, r.Permissions, r.Pages))
            .ToList();
        return new RuntimeConfigDto(
            tenant.Name,
            tenant.Slug,
            tenant.Kind,
            preset.Key,
            tenant.ThemeMode,
            ThemeTokens.From(preset.Light),
            ThemeTokens.From(preset.Dark),
            screens,
            activeRoles);
    }
}
