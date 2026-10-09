using Recuro.Admin.Domain.Common;
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

/// <summary>A workspace owner to create in the account directory (Keycloak), signed in with email and password.</summary>
public sealed record NewWorkspaceOwner(Guid TenantId, string Name, string Email, string Role, string Password);

/// <summary>
/// The people directory that signs users in (Keycloak). Accounts live there, never in the admin database,
/// so passwords stay with the identity provider.
/// </summary>
public interface IAccountDirectory
{
    /// <summary>Creates the account with its persona role and tenant-admin rights; returns the account id (Keycloak subject).</summary>
    Task<Result<string>> CreateWorkspaceOwnerAsync(NewWorkspaceOwner owner, CancellationToken ct);

    /// <summary>Removes an account created by a sign-up that could not be completed.</summary>
    Task DeleteAccountAsync(string accountId, CancellationToken ct);
}

/// <summary>Locale and currency a self-service workspace starts with, set in configuration ("SignUp" section).</summary>
public sealed record SignUpDefaults(string Locale, string Currency);
