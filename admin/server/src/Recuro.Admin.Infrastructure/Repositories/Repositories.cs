using Microsoft.EntityFrameworkCore;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Roles;
using Recuro.Admin.Domain.Screens;
using Recuro.Admin.Domain.Tenants;
using Recuro.Admin.Domain.Theming;
using Recuro.Admin.Infrastructure.Persistence;

namespace Recuro.Admin.Infrastructure.Repositories;

internal sealed class TenantRepository(AdminDbContext db) : ITenantRepository
{
    public async Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken ct) =>
        await db.Tenants.OrderBy(t => t.Name).ToListAsync(ct);

    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct)
    {
        var normalised = slug.Trim().ToLowerInvariant();
        return db.Tenants.FirstOrDefaultAsync(t => t.Slug == normalised, ct);
    }

    public async Task<IReadOnlyList<Tenant>> ListActiveByNameAsync(string name, CancellationToken ct)
    {
        var normalised = name.Trim().ToLowerInvariant();
#pragma warning disable CA1304, CA1311, CA1862 // runs as SQL lower() in the database, where culture doesn't apply
        return await db.Tenants
            .Where(t => t.Status == TenantStatus.Active && t.Name.ToLower() == normalised)
            .Take(2)
            .ToListAsync(ct);
#pragma warning restore CA1304, CA1311, CA1862
    }

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) =>
        db.Tenants.AnyAsync(t => t.Slug == slug, ct);

    public void Add(Tenant tenant) => db.Tenants.Add(tenant);
}

internal sealed class ThemePresetRepository(AdminDbContext db) : IThemePresetRepository
{
    public async Task<IReadOnlyList<ThemePreset>> ListAsync(bool publishedOnly, CancellationToken ct) =>
        await db.ThemePresets
            .Where(p => !publishedOnly || p.IsPublished)
            .OrderBy(p => p.SortOrder)
            .ToListAsync(ct);

    public Task<ThemePreset?> GetByKeyAsync(string key, CancellationToken ct)
    {
        var normalised = key.Trim().ToLowerInvariant();
        return db.ThemePresets.FirstOrDefaultAsync(p => p.Key == normalised, ct);
    }

    public Task<bool> KeyExistsAsync(string key, CancellationToken ct) =>
        db.ThemePresets.AnyAsync(p => p.Key == key, ct);

    public void Add(ThemePreset preset) => db.ThemePresets.Add(preset);
}

internal sealed class ScreenCatalog(AdminDbContext db) : IScreenCatalog
{
    public async Task<IReadOnlyList<ScreenDefinition>> ListAsync(CancellationToken ct) =>
        await db.ScreenDefinitions.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);

    public Task<ScreenDefinition?> GetByKeyAsync(string key, CancellationToken ct) =>
        db.ScreenDefinitions.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
}

/// <summary>
/// Every method takes the tenant id explicitly and filters on it, so it also serves the anonymous
/// runtime-config read (which has no tenant on the request). The global query filter is bypassed only here.
/// </summary>
internal sealed class ScreenConfigurationRepository(AdminDbContext db) : IScreenConfigurationRepository
{
    public async Task<IReadOnlyList<TenantScreenConfiguration>> ListForTenantAsync(Guid tenantId, CancellationToken ct) =>
        await db.TenantScreenConfigurations.IgnoreQueryFilters().Where(c => c.TenantId == tenantId).ToListAsync(ct);

    public Task<TenantScreenConfiguration?> GetAsync(Guid tenantId, string screenKey, CancellationToken ct) =>
        db.TenantScreenConfigurations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ScreenKey == screenKey, ct);

    public void Add(TenantScreenConfiguration configuration) => db.TenantScreenConfigurations.Add(configuration);
}

/// <summary>
/// Takes the tenant explicitly so platform admins (no tenant on the request) can manage any workspace's roles;
/// a tenant admin is still held to their own tenant by TenantScope and the DbContext's write guard.
/// </summary>
internal sealed class WorkspaceRoleRepository(AdminDbContext db) : IWorkspaceRoleRepository
{
    public async Task<IReadOnlyList<WorkspaceRole>> ListAsync(Guid tenantId, CancellationToken ct) =>
        await db.WorkspaceRoles.IgnoreQueryFilters().Where(r => r.TenantId == tenantId).OrderBy(r => r.SortOrder).ThenBy(r => r.Key).ToListAsync(ct);

    public Task<bool> AnyAsync(Guid tenantId, CancellationToken ct) =>
        db.WorkspaceRoles.IgnoreQueryFilters().AnyAsync(r => r.TenantId == tenantId, ct);

    public void AddRange(IEnumerable<WorkspaceRole> roles) => db.WorkspaceRoles.AddRange(roles);
}

internal sealed class WorkspaceMemberRepository(AdminDbContext db) : IWorkspaceMemberRepository
{
    public async Task<IReadOnlyList<WorkspaceMember>> ListAsync(Guid tenantId, CancellationToken ct) =>
        await db.WorkspaceMembers.IgnoreQueryFilters().Where(m => m.TenantId == tenantId).ToListAsync(ct);

    public Task<WorkspaceMember?> GetAsync(Guid tenantId, string accountId, CancellationToken ct) =>
        db.WorkspaceMembers.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.TenantId == tenantId && m.AccountId == accountId, ct);

    public void Add(WorkspaceMember member) => db.WorkspaceMembers.Add(member);
}
