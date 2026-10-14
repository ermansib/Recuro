using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Application.Roles;

/// <summary>A role the signed-in person holds, as the main portal needs it to shape its screens.</summary>
public sealed record MyRoleDto(string Key, string Name, IReadOnlyList<string> Permissions, IReadOnlyList<string> Pages, bool RequiresMfa);

/// <summary>
/// The signed-in person's access in their own workspace: the active roles they hold and, for convenience, the union of
/// those roles' permissions and pages. The portal uses it only to shape the UI; services still decide every action.
/// </summary>
public sealed record MyAccessDto(Guid WorkspaceId, IReadOnlyList<MyRoleDto> Roles, IReadOnlyList<string> Permissions, IReadOnlyList<string> Pages);

/// <summary>Who is asking: the workspace and account from their token, and the role keys the token carries.</summary>
public sealed record AccessRequester(Guid WorkspaceId, string AccountId, IReadOnlyCollection<string> TokenRoleKeys);

/// <summary>
/// Resolves the caller's roles from the workspace's role assignments (the admin database is the source of truth). A person
/// the workspace has no member row for yet, such as a new owner, falls back to the role keys their token carries.
/// </summary>
public sealed class GetMyAccessHandler(ITenantRepository tenants, IWorkspaceRoleRepository roles, IWorkspaceMemberRepository members)
{
    public async Task<Result<MyAccessDto>> HandleAsync(AccessRequester requester, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requester);
        var tenant = await tenants.GetByIdAsync(requester.WorkspaceId, ct);
        if (tenant is null || tenant.Status != TenantStatus.Active)
        {
            return TenantErrors.NotFound(requester.WorkspaceId);
        }

        var member = await members.GetAsync(requester.WorkspaceId, requester.AccountId, ct);
        var held = (member?.RoleKeys ?? [.. requester.TokenRoleKeys]).ToHashSet(StringComparer.Ordinal);
        var mine = (await roles.ListAsync(requester.WorkspaceId, ct))
            .Where(r => r.IsActive && held.Contains(r.Key))
            .OrderBy(r => r.SortOrder)
            .Select(r => new MyRoleDto(r.Key, r.Name, r.Permissions, r.Pages, r.RequiresMfa))
            .ToList();

        return new MyAccessDto(
            requester.WorkspaceId,
            mine,
            [.. mine.SelectMany(r => r.Permissions).Distinct(StringComparer.Ordinal)],
            [.. mine.SelectMany(r => r.Pages).Distinct(StringComparer.Ordinal)]);
    }
}
