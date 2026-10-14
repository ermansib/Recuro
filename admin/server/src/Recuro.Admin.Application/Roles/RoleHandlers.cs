using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Roles;
using Recuro.Admin.Domain.Screens;

namespace Recuro.Admin.Application.Roles;

/// <summary>The permissions and screens a role can be given.</summary>
public sealed class GetRoleCatalogHandler(IScreenCatalog screens)
{
    public async Task<RoleCatalogDto> HandleAsync(CancellationToken ct)
    {
        var pages = (await screens.ListAsync(ct)).Select(s => new RolePageDto(s.Key, s.Code, s.DefaultTitle, s.Module)).ToList();
        return new RoleCatalogDto(PermissionCatalog.All, pages);
    }
}

/// <summary>
/// Every role of a workspace with how many people hold it. Tenant admins see their own workspace;
/// platform admins pass the tenant id.
/// </summary>
public sealed class ListRolesHandler(
    ITenantContext tenantContext,
    ITenantRepository tenants,
    IWorkspaceRoleRepository roles,
    IWorkspaceMemberRepository members)
{
    public async Task<Result<IReadOnlyList<RoleDto>>> HandleAsync(Guid? tenantId, CancellationToken ct)
    {
        var tenant = await TenantScope.ResolveAsync(tenantContext, tenants, tenantId, ct);
        if (tenant.IsFailure)
        {
            return tenant.Error!;
        }

        var counts = RoleRules.MemberCounts(await members.ListAsync(tenant.Value, ct));
        var list = (await roles.ListAsync(tenant.Value, ct)).Select(r => RoleDto.From(r, counts.GetValueOrDefault(r.Key))).ToList();
        return Result.Success<IReadOnlyList<RoleDto>>(list);
    }
}

public sealed class CreateRoleHandler(
    ITenantContext tenantContext,
    ITenantRepository tenants,
    IWorkspaceRoleRepository roles,
    IScreenCatalog screens,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<RoleDto>> HandleAsync(Guid? tenantId, CreateRoleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = await TenantScope.ResolveAsync(tenantContext, tenants, tenantId, ct);
        if (tenant.IsFailure)
        {
            return tenant.Error!;
        }

        var existing = await roles.ListAsync(tenant.Value, ct);
        var sortOrder = existing.Count == 0 ? 10 : existing.Max(r => r.SortOrder) + 10;
        var created = WorkspaceRole.Create(tenant.Value, request.Key, RoleRules.ChangesFrom(request), sortOrder);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var role = created.Value;
        if (existing.Any(r => r.Key == role.Key))
        {
            return RoleErrors.KeyTaken(role.Key);
        }

        var invalid = await RoleRules.CheckAsync(role, existing, screens, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        roles.AddRange([role]);
        await RoleRules.SyncMfaRolesAsync(tenants, tenant.Value, [.. existing, role], ct);
        await unitOfWork.SaveChangesAsync(ct);
        return RoleDto.From(role, 0);
    }
}

public sealed class UpdateRoleHandler(
    ITenantContext tenantContext,
    ITenantRepository tenants,
    IWorkspaceRoleRepository roles,
    IWorkspaceMemberRepository members,
    IScreenCatalog screens,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<RoleDto>> HandleAsync(Guid? tenantId, string key, UpdateRoleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = await TenantScope.ResolveAsync(tenantContext, tenants, tenantId, ct);
        if (tenant.IsFailure)
        {
            return tenant.Error!;
        }

        var all = await roles.ListAsync(tenant.Value, ct);
        var role = all.FirstOrDefault(r => r.Key == key);
        if (role is null)
        {
            return RoleErrors.NotFound(key);
        }

        var updated = role.Update(new RoleChanges(request.Name, request.Description, request.ReportsTo, request.Permissions, request.Pages, request.RequiresMfa));
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        var invalid = await RoleRules.CheckAsync(role, all, screens, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        await RoleRules.SyncMfaRolesAsync(tenants, tenant.Value, all, ct);
        await unitOfWork.SaveChangesAsync(ct);
        var counts = RoleRules.MemberCounts(await members.ListAsync(tenant.Value, ct));
        return RoleDto.From(role, counts.GetValueOrDefault(role.Key));
    }
}

/// <summary>Switches a role on or off. A role still held by someone, or that others report to, stays on.</summary>
public sealed class SetRoleStatusHandler(
    ITenantContext tenantContext,
    ITenantRepository tenants,
    IWorkspaceRoleRepository roles,
    IWorkspaceMemberRepository members,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<RoleDto>> HandleAsync(Guid? tenantId, string key, SetRoleStatusRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = await TenantScope.ResolveAsync(tenantContext, tenants, tenantId, ct);
        if (tenant.IsFailure)
        {
            return tenant.Error!;
        }

        var all = await roles.ListAsync(tenant.Value, ct);
        var role = all.FirstOrDefault(r => r.Key == key);
        if (role is null)
        {
            return RoleErrors.NotFound(key);
        }

        var counts = RoleRules.MemberCounts(await members.ListAsync(tenant.Value, ct));
        var holders = counts.GetValueOrDefault(key);
        if (!request.IsActive && holders > 0)
        {
            return RoleErrors.InUse(key, holders);
        }

        var report = all.FirstOrDefault(r => r.IsActive && r.ReportsTo == key);
        if (!request.IsActive && report is not null)
        {
            return RoleErrors.HasReports(key, report.Key);
        }

        var changed = role.SetActive(request.IsActive);
        if (changed.IsFailure)
        {
            return changed.Error!;
        }

        await RoleRules.SyncMfaRolesAsync(tenants, tenant.Value, all, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return RoleDto.From(role, holders);
    }
}

/// <summary>Checks that need the rest of the workspace: page keys, and a reporting line that exists and doesn't loop.</summary>
internal static class RoleRules
{
    public static RoleChanges ChangesFrom(CreateRoleRequest request) =>
        new(request.Name, request.Description, request.ReportsTo, request.Permissions, request.Pages, request.RequiresMfa);

    public static Dictionary<string, int> MemberCounts(IEnumerable<WorkspaceMember> members) =>
        members.SelectMany(m => m.RoleKeys).GroupBy(k => k, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    public static async Task<Error?> CheckAsync(WorkspaceRole role, IReadOnlyList<WorkspaceRole> workspaceRoles, IScreenCatalog screens, CancellationToken ct)
    {
        var pageKeys = (await screens.ListAsync(ct)).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var unknownPage = role.Pages.FirstOrDefault(p => !pageKeys.Contains(p));
        if (unknownPage is not null)
        {
            return RoleErrors.UnknownPage(unknownPage);
        }

        if (role.ReportsTo is null)
        {
            return null;
        }

        var byKey = workspaceRoles.Where(r => r.Key != role.Key).ToDictionary(r => r.Key, StringComparer.Ordinal);
        if (!byKey.TryGetValue(role.ReportsTo, out var parent) || !parent.IsActive)
        {
            return RoleErrors.UnknownReportsTo(role.ReportsTo);
        }

        // Walk up the chain; meeting this role again means the hierarchy would loop.
        var seen = new HashSet<string>(StringComparer.Ordinal) { role.Key };
        for (var current = parent; current is not null; current = current.ReportsTo is { } next ? byKey.GetValueOrDefault(next) : null)
        {
            if (!seen.Add(current.Key) || current.ReportsTo == role.Key)
            {
                return RoleErrors.HierarchyCycle(role.ReportsTo);
            }
        }

        return null;
    }

    /// <summary>The workspace's sign-in MFA list follows its active roles marked "requires MFA".</summary>
    public static async Task SyncMfaRolesAsync(ITenantRepository tenants, Guid tenantId, IEnumerable<WorkspaceRole> workspaceRoles, CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(tenantId, ct);
        tenant?.SetMfaRoles(workspaceRoles.Where(r => r.IsActive && r.RequiresMfa).Select(r => r.Key));
    }
}
