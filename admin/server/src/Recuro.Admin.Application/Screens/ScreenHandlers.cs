using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Screens;

namespace Recuro.Admin.Application.Screens;

/// <summary>Lists every screen of the main portal as the current tenant's users will see it.</summary>
public sealed class ListScreensHandler(ITenantContext tenantContext, IScreenCatalog catalog, IScreenConfigurationRepository configurations)
{
    public async Task<Result<IReadOnlyList<EffectiveScreen>>> HandleAsync(CancellationToken ct)
    {
        var tenantId = TenantScope.Require(tenantContext);
        if (tenantId.IsFailure)
        {
            return tenantId.Error!;
        }

        var screens = await ResolveAllAsync(catalog, configurations, tenantId.Value, ct);
        return Result.Success(screens);
    }

    internal static async Task<IReadOnlyList<EffectiveScreen>> ResolveAllAsync(
        IScreenCatalog catalog, IScreenConfigurationRepository configurations, Guid tenantId, CancellationToken ct)
    {
        var definitions = await catalog.ListAsync(ct);
        var overrides = (await configurations.ListForTenantAsync(tenantId, ct)).ToDictionary(c => c.ScreenKey, StringComparer.Ordinal);
        return definitions
            .Select(definition => ScreenLayout.Resolve(definition, overrides.GetValueOrDefault(definition.Key)))
            .ToList();
    }
}

public sealed class GetScreenHandler(ITenantContext tenantContext, IScreenCatalog catalog, IScreenConfigurationRepository configurations)
{
    public async Task<Result<EffectiveScreen>> HandleAsync(string screenKey, CancellationToken ct)
    {
        var tenantId = TenantScope.Require(tenantContext);
        if (tenantId.IsFailure)
        {
            return tenantId.Error!;
        }

        var definition = await catalog.GetByKeyAsync(screenKey, ct);
        if (definition is null)
        {
            return ScreenErrors.NotFound(screenKey);
        }

        var configuration = await configurations.GetAsync(tenantId.Value, screenKey, ct);
        return ScreenLayout.Resolve(definition, configuration);
    }
}

public sealed record UpdateFieldRequest(string Key, string? Label, bool IsVisible, bool IsRequired, int SortOrder);

public sealed record UpdateScreenRequest(bool IsEnabled, string? Title, string? Subtitle, IReadOnlyList<UpdateFieldRequest> Fields);

public sealed class UpdateScreenHandler(
    ITenantContext tenantContext,
    IScreenCatalog catalog,
    IScreenConfigurationRepository configurations,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<EffectiveScreen>> HandleAsync(string screenKey, UpdateScreenRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenantId = TenantScope.Require(tenantContext);
        if (tenantId.IsFailure)
        {
            return tenantId.Error!;
        }

        var definition = await catalog.GetByKeyAsync(screenKey, ct);
        if (definition is null)
        {
            return ScreenErrors.NotFound(screenKey);
        }

        var configuration = await configurations.GetAsync(tenantId.Value, screenKey, ct);
        var isNew = configuration is null;
        configuration ??= TenantScreenConfiguration.CreateFor(tenantId.Value, screenKey);

        var changes = new ScreenChanges(
            request.IsEnabled,
            request.Title,
            request.Subtitle,
            (request.Fields ?? []).Select(f => new TenantFieldSetting(f.Key, f.Label, f.IsVisible, f.IsRequired, f.SortOrder)).ToList());

        var result = configuration.Update(definition, changes);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        if (isNew)
        {
            configurations.Add(configuration);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return ScreenLayout.Resolve(definition, configuration);
    }
}
