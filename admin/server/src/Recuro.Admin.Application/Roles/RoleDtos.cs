using Recuro.Admin.Domain.Roles;

namespace Recuro.Admin.Application.Roles;

/// <summary>A workspace role as the admin portal shows it.</summary>
public sealed record RoleDto(
    string Key,
    string Name,
    string? Description,
    string? ReportsTo,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Pages,
    bool RequiresMfa,
    bool IsSystem,
    bool IsActive,
    int MemberCount)
{
    public static RoleDto From(WorkspaceRole role, int memberCount)
    {
        ArgumentNullException.ThrowIfNull(role);
        return new(role.Key, role.Name, role.Description, role.ReportsTo, role.Permissions, role.Pages, role.RequiresMfa, role.IsSystem, role.IsActive, memberCount);
    }
}

/// <summary>A screen a role can be given access to.</summary>
public sealed record RolePageDto(string Key, string Code, string Title, string Module);

/// <summary>What a role can be made of: the product's permissions and screens.</summary>
public sealed record RoleCatalogDto(IReadOnlyList<PermissionDefinition> Permissions, IReadOnlyList<RolePageDto> Pages);

public sealed record CreateRoleRequest(
    string Key,
    string Name,
    string? Description,
    string? ReportsTo,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Pages,
    bool RequiresMfa);

public sealed record UpdateRoleRequest(
    string Name,
    string? Description,
    string? ReportsTo,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Pages,
    bool RequiresMfa);

public sealed record SetRoleStatusRequest(bool IsActive);

/// <summary>A person in the workspace. <see cref="AccountId"/> is the identity provider's subject id.</summary>
public sealed record MemberDto(string AccountId, string Name, string Email, IReadOnlyList<string> RoleKeys, string? Department)
{
    public static MemberDto From(WorkspaceMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return new(member.AccountId, member.Name, member.Email, member.RoleKeys, member.Department);
    }
}

public sealed record UpdateMemberRequest(IReadOnlyList<string> RoleKeys, string? Department);
