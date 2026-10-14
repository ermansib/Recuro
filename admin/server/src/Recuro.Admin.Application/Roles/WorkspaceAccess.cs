using System.Security.Cryptography;
using System.Text;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Application.Roles;

/// <summary>A workspace role as the microservices need it to decide access.</summary>
public sealed record AccessRoleDto(
    string Key,
    string Name,
    string? ReportsTo,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Pages,
    bool RequiresMfa,
    bool IsActive);

public sealed record AccessMemberDto(string AccountId, IReadOnlyList<string> RoleKeys, string? Department);

/// <summary>
/// Everything the Identity service needs to decide access in one workspace: its roles and who holds them.
/// <see cref="Version"/> changes whenever any of it changes, so callers can key caches and decisions on it.
/// </summary>
public sealed record WorkspaceAccessDto(Guid WorkspaceId, string Version, IReadOnlyList<AccessRoleDto> Roles, IReadOnlyList<AccessMemberDto> Members);

/// <summary>Internal read for Recuro microservices (service-account tokens only).</summary>
public sealed class GetWorkspaceAccessHandler(ITenantRepository tenants, IWorkspaceRoleRepository roles, IWorkspaceMemberRepository members)
{
    public async Task<Result<WorkspaceAccessDto>> HandleAsync(Guid workspaceId, CancellationToken ct)
    {
        if (await tenants.GetByIdAsync(workspaceId, ct) is null)
        {
            return TenantErrors.NotFound(workspaceId);
        }

        var roleRows = (await roles.ListAsync(workspaceId, ct))
            .Select(r => new AccessRoleDto(r.Key, r.Name, r.ReportsTo, r.Permissions, r.Pages, r.RequiresMfa, r.IsActive))
            .ToList();
        var memberRows = (await members.ListAsync(workspaceId, ct))
            .OrderBy(m => m.AccountId, StringComparer.Ordinal)
            .Select(m => new AccessMemberDto(m.AccountId, m.RoleKeys, m.Department))
            .ToList();
        return new WorkspaceAccessDto(workspaceId, VersionOf(roleRows, memberRows), roleRows, memberRows);
    }

    private static string VersionOf(IEnumerable<AccessRoleDto> roleRows, IEnumerable<AccessMemberDto> memberRows)
    {
        var text = new StringBuilder();
        foreach (var r in roleRows)
        {
            text.Append(r.Key).Append('|').Append(r.ReportsTo).Append('|').AppendJoin(',', r.Permissions).Append('|')
                .AppendJoin(',', r.Pages).Append('|').Append(r.RequiresMfa).Append('|').Append(r.IsActive).Append(';');
        }

        foreach (var m in memberRows)
        {
            text.Append(m.AccountId).Append('|').AppendJoin(',', m.RoleKeys).Append('|').Append(m.Department).Append(';');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return $"roles-{Convert.ToHexStringLower(hash)[..12]}";
    }
}
