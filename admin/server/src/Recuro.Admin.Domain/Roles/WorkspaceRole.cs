using System.Text.RegularExpressions;
using Recuro.Admin.Domain.Common;

namespace Recuro.Admin.Domain.Roles;

/// <summary>What a tenant admin can set on a role. The key is fixed once the role exists.</summary>
public sealed record RoleChanges(
    string Name,
    string? Description,
    string? ReportsTo,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Pages,
    bool RequiresMfa);

/// <summary>
/// A role in one workspace (tenant): what its holders may do (permissions), which screens they see (pages) and
/// whom the role reports to (hierarchy, for example HOD → HR Head). Roles are data the tenant edits in the admin
/// portal, not code. The key is what tokens, workflow assignee/escalateTo rules and the policy decision point use.
/// </summary>
public sealed partial class WorkspaceRole : Entity, ITenantOwned
{
    public const int KeyMaxLength = 40;
    public const int NameMaxLength = 60;
    public const int DescriptionMaxLength = 300;

    /// <summary>Keys that already mean something to the platform or Keycloak and so can't be a workspace role.</summary>
    private static readonly HashSet<string> ReservedKeys = new(StringComparer.Ordinal)
    {
        "platform-admin", "tenant-admin", "candidate", "service", "admin", "offline_access", "uma_authorization",
    };

    private WorkspaceRole()
    {
    }

    public Guid TenantId { get; private set; }

    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Key of the role this one reports to, or null at the top of the hierarchy.</summary>
    public string? ReportsTo { get; private set; }

    /// <summary>Permission keys from <see cref="PermissionCatalog"/>.</summary>
    public IReadOnlyList<string> Permissions { get; private set; } = [];

    /// <summary>Screen keys (the admin screen catalogue) holders of this role can open.</summary>
    public IReadOnlyList<string> Pages { get; private set; } = [];

    /// <summary>Holders must pass a second factor at sign-in and before sensitive actions (RCU-PLT-001, RCU-AUT-002).</summary>
    public bool RequiresMfa { get; private set; }

    /// <summary>One of the roles every workspace starts with. It can be changed or switched off, never deleted.</summary>
    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; } = true;

    public int SortOrder { get; private set; }

    [GeneratedRegex("^[a-z][a-z0-9-]*[a-z0-9]$")]
    private static partial Regex KeyPattern();

    public static Result<WorkspaceRole> Create(Guid tenantId, string key, RoleChanges changes, int sortOrder)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var normalisedKey = key?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalisedKey.Length is < 2 or > KeyMaxLength || !KeyPattern().IsMatch(normalisedKey))
        {
            return RoleErrors.InvalidKey;
        }

        if (ReservedKeys.Contains(normalisedKey) || normalisedKey.StartsWith("default-roles", StringComparison.Ordinal))
        {
            return RoleErrors.ReservedKey(normalisedKey);
        }

        var role = new WorkspaceRole { Id = Guid.NewGuid(), TenantId = tenantId, Key = normalisedKey, SortOrder = sortOrder };
        var applied = role.Update(changes);
        return applied.IsFailure ? applied.Error! : role;
    }

    /// <summary>Copies a default role into a new workspace.</summary>
    public static WorkspaceRole FromDefault(Guid tenantId, DefaultRoleSpec spec, int sortOrder)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return new WorkspaceRole
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Key = spec.Key,
            Name = spec.Name,
            Description = spec.Description,
            ReportsTo = spec.ReportsTo,
            Permissions = [.. spec.Permissions],
            Pages = [.. spec.Pages],
            RequiresMfa = spec.RequiresMfa,
            IsSystem = true,
            SortOrder = sortOrder,
        };
    }

    /// <summary>
    /// Replaces the role's settings. Page keys and the reporting line are checked against the workspace by the
    /// caller, which knows the screen catalogue and the other roles.
    /// </summary>
    public Result Update(RoleChanges changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var name = changes.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > NameMaxLength)
        {
            return RoleErrors.InvalidName;
        }

        var description = string.IsNullOrWhiteSpace(changes.Description) ? null : changes.Description.Trim();
        if (description?.Length > DescriptionMaxLength)
        {
            return RoleErrors.DescriptionTooLong;
        }

        var permissions = Distinct(changes.Permissions);
        var unknown = permissions.FirstOrDefault(p => !PermissionCatalog.Contains(p));
        if (unknown is not null)
        {
            return RoleErrors.UnknownPermission(unknown);
        }

        var reportsTo = string.IsNullOrWhiteSpace(changes.ReportsTo) ? null : changes.ReportsTo.Trim().ToLowerInvariant();
        if (reportsTo == Key)
        {
            return RoleErrors.ReportsToItself;
        }

        Name = name;
        Description = description;
        ReportsTo = reportsTo;
        Permissions = permissions;
        Pages = Distinct(changes.Pages);
        RequiresMfa = changes.RequiresMfa;
        return Result.Success();
    }

    public Result SetActive(bool isActive)
    {
        if (IsActive == isActive)
        {
            return RoleErrors.AlreadyInStatus(Key, isActive);
        }

        IsActive = isActive;
        return Result.Success();
    }

    public bool HasPermission(string permission) => IsActive && Permissions.Contains(permission, StringComparer.Ordinal);

    private static List<string> Distinct(IReadOnlyList<string>? keys) =>
        (keys ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct(StringComparer.Ordinal).ToList();
}

public static class RoleErrors
{
    public static readonly Error InvalidKey = Error.Validation(
        "role.key",
        $"Role key must be 2 to {WorkspaceRole.KeyMaxLength} lowercase letters, digits or hyphens, starting with a letter.");

    public static readonly Error InvalidName =
        Error.Validation("role.name", $"Role name is required and must be at most {WorkspaceRole.NameMaxLength} characters.");

    public static readonly Error DescriptionTooLong =
        Error.Validation("role.description", $"Description must be at most {WorkspaceRole.DescriptionMaxLength} characters.");

    public static readonly Error ReportsToItself =
        Error.Validation("role.reportsTo", "A role cannot report to itself.");

    public static readonly Error NoRoles =
        Error.Validation("member.roles", "Give the person at least one role.");

    public static Error ReservedKey(string key) =>
        Error.Validation("role.reservedKey", $"'{key}' is reserved by the platform. Choose another key.");

    public static Error KeyTaken(string key) =>
        Error.Conflict("role.keyTaken", $"This workspace already has a role with the key '{key}'.");

    public static Error UnknownPermission(string permission) =>
        Error.Validation("role.unknownPermission", $"'{permission}' is not a permission Recuro knows.");

    public static Error UnknownPage(string page) =>
        Error.Validation("role.unknownPage", $"'{page}' is not a screen of the portal.");

    public static Error UnknownReportsTo(string key) =>
        Error.Validation("role.reportsTo", $"'{key}' is not an active role in this workspace.");

    public static Error HierarchyCycle(string key) =>
        Error.Validation("role.hierarchyCycle", $"Reporting to '{key}' would make the hierarchy loop back to this role.");

    public static Error NotFound(string key) => Error.NotFound("role.notFound", $"Role '{key}' was not found.");

    public static Error Inactive(string key) => Error.Validation("member.inactiveRole", $"Role '{key}' is switched off.");

    public static Error InUse(string key, int members) =>
        Error.Conflict("role.inUse", $"{members} people still hold the role '{key}'. Move them to another role first.");

    public static Error HasReports(string key, string report) =>
        Error.Conflict("role.hasReports", $"Role '{report}' reports to '{key}'. Change its reporting line first.");

    public static Error AlreadyInStatus(string key, bool isActive) =>
        Error.Conflict("role.status", $"Role '{key}' is already {(isActive ? "active" : "switched off")}.");
}
