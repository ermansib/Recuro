using Recuro.Admin.Domain.Screens;
using Recuro.Admin.Domain.Tenants;
using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.Application.Abstractions;

/// <summary>The tenant the current request acts for, resolved once per request from the signed-in user's claims.</summary>
public interface ITenantContext
{
    /// <summary>Null for platform administrators, who act across tenants.</summary>
    Guid? TenantId { get; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}

public interface ITenantRepository
{
    Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken ct);

    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct);

    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);

    void Add(Tenant tenant);
}

public interface IThemePresetRepository
{
    Task<IReadOnlyList<ThemePreset>> ListAsync(bool publishedOnly, CancellationToken ct);

    Task<ThemePreset?> GetByKeyAsync(string key, CancellationToken ct);

    Task<bool> KeyExistsAsync(string key, CancellationToken ct);

    void Add(ThemePreset preset);
}

/// <summary>Read-only catalogue of the main portal's screens and fields.</summary>
public interface IScreenCatalog
{
    Task<IReadOnlyList<ScreenDefinition>> ListAsync(CancellationToken ct);

    Task<ScreenDefinition?> GetByKeyAsync(string key, CancellationToken ct);
}

public interface IScreenConfigurationRepository
{
    Task<IReadOnlyList<TenantScreenConfiguration>> ListForTenantAsync(Guid tenantId, CancellationToken ct);

    Task<TenantScreenConfiguration?> GetAsync(Guid tenantId, string screenKey, CancellationToken ct);

    void Add(TenantScreenConfiguration configuration);
}
