using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Roles;
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

    /// <summary>Active workspaces whose name matches, ignoring case and surrounding spaces.</summary>
    Task<IReadOnlyList<Tenant>> ListActiveByNameAsync(string name, CancellationToken ct);

    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);

    Task<bool> CustomDomainExistsAsync(string domain, CancellationToken ct);

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

/// <summary>
/// A workspace owner to create in the account directory (Keycloak), signed in with email and password.
/// <paramref name="MustChangePassword"/> makes the password temporary: the owner sets their own at first sign-in.
/// </summary>
public sealed record NewWorkspaceOwner(Guid TenantId, string Name, string Email, string Role, string Password, bool MustChangePassword = false);

/// <summary>
/// A person a workspace admin adds to their workspace. <paramref name="TemporaryPassword"/> is replaced by the
/// person at first sign-in; <paramref name="RoleKeys"/> become the <c>workspace_roles</c> attribute.
/// </summary>
public sealed record NewWorkspacePerson(Guid TenantId, string Name, string Email, IReadOnlyList<string> RoleKeys, string TemporaryPassword);

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

    /// <summary>
    /// People whose account belongs to the tenant. Realm roles are read only for accounts not in
    /// <paramref name="knownAccountIds"/>, so a listing stays cheap once everyone has been imported.
    /// </summary>
    Task<Result<IReadOnlyList<DirectoryAccount>>> ListTenantAccountsAsync(
        Guid tenantId, IReadOnlySet<string> knownAccountIds, CancellationToken ct);

    /// <summary>
    /// Mirrors a person's workspace roles onto their account, so the next token carries them: the
    /// <c>workspace_roles</c> attribute always, and the realm role of the same name where one exists.
    /// Realm roles in <paramref name="workspaceRoleKeys"/> that the person no longer holds are removed.
    /// </summary>
    Task<Result> SyncWorkspaceRolesAsync(
        string accountId, IReadOnlyList<string> roleKeys, IReadOnlyCollection<string> workspaceRoleKeys, CancellationToken ct);

    /// <summary>
    /// Creates a person's account in the workspace with a temporary password, their workspace roles (attribute,
    /// plus realm roles of the same name where they exist) and no admin rights. Returns the account id.
    /// </summary>
    Task<Result<string>> CreateWorkspacePersonAsync(NewWorkspacePerson person, CancellationToken ct);

    /// <summary>
    /// Sets a temporary password the person must replace at their next sign-in. Refused with not found when the
    /// account does not belong to <paramref name="tenantId"/>.
    /// </summary>
    Task<Result> SetTemporaryPasswordAsync(Guid tenantId, string accountId, string password, CancellationToken ct);
}

/// <summary>An account in the identity provider. <see cref="RealmRoles"/> is empty when it wasn't read.</summary>
public sealed record DirectoryAccount(string Id, string Name, string Email, IReadOnlyList<string> RealmRoles);

/// <summary>Roles of each workspace. Every method takes the tenant explicitly, so platform admins can use it too.</summary>
public interface IWorkspaceRoleRepository
{
    Task<IReadOnlyList<WorkspaceRole>> ListAsync(Guid tenantId, CancellationToken ct);

    Task<bool> AnyAsync(Guid tenantId, CancellationToken ct);

    void AddRange(IEnumerable<WorkspaceRole> roles);
}

/// <summary>People of each workspace and their roles. Every method takes the tenant explicitly.</summary>
public interface IWorkspaceMemberRepository
{
    Task<IReadOnlyList<WorkspaceMember>> ListAsync(Guid tenantId, CancellationToken ct);

    Task<WorkspaceMember?> GetAsync(Guid tenantId, string accountId, CancellationToken ct);

    void Add(WorkspaceMember member);
}

/// <summary>Locale and currency a self-service workspace starts with, set in configuration ("SignUp" section).</summary>
public sealed record SignUpDefaults(string Locale, string Currency);
