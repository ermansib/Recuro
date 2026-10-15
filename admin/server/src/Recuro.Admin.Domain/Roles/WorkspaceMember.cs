using Recuro.Admin.Domain.Common;

namespace Recuro.Admin.Domain.Roles;

/// <summary>
/// A person in a workspace and the roles the tenant gave them. The account itself (password, MFA) lives in the
/// identity provider; this row is the source of truth for role assignment and is synced to it.
/// </summary>
public sealed class WorkspaceMember : Entity, ITenantOwned
{
    public const int NameMaxLength = 120;
    public const int EmailMaxLength = 254;
    public const int DepartmentMaxLength = 80;

    private WorkspaceMember()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>The identity provider's subject id (Keycloak user id).</summary>
    public string AccountId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public IReadOnlyList<string> RoleKeys { get; private set; } = [];

    /// <summary>Department the person belongs to, for department-scoped roles such as HOD.</summary>
    public string? Department { get; private set; }

    public static WorkspaceMember Create(Guid tenantId, string accountId, string name, string email, IReadOnlyList<string> roleKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AccountId = accountId,
            Name = Truncate(name?.Trim() ?? string.Empty, NameMaxLength),
            Email = Truncate(email?.Trim().ToLowerInvariant() ?? string.Empty, EmailMaxLength),
            RoleKeys = [.. (roleKeys ?? []).Distinct(StringComparer.Ordinal)],
        };
    }

    /// <summary>Sets the person's roles; every key must be an active role of the workspace.</summary>
    public Result Assign(IReadOnlyList<string> roleKeys, string? department, IReadOnlyCollection<WorkspaceRole> workspaceRoles)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoles);
        var keys = (roleKeys ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0)
        {
            return RoleErrors.NoRoles;
        }

        foreach (var key in keys)
        {
            var role = workspaceRoles.FirstOrDefault(r => r.Key == key);
            if (role is null)
            {
                return RoleErrors.NotFound(key);
            }

            // Keeping a role someone already has is fine even if it was switched off; newly giving it is not.
            if (!role.IsActive && !RoleKeys.Contains(key, StringComparer.Ordinal))
            {
                return RoleErrors.Inactive(key);
            }
        }

        var trimmedDepartment = string.IsNullOrWhiteSpace(department) ? null : department.Trim();
        if (trimmedDepartment?.Length > DepartmentMaxLength)
        {
            return Error.Validation("member.department", $"Department must be at most {DepartmentMaxLength} characters.");
        }

        RoleKeys = keys;
        Department = trimmedDepartment;
        return Result.Success();
    }

    /// <summary>Refreshes the display details from the identity provider.</summary>
    public void Rename(string name, string email)
    {
        Name = Truncate(name?.Trim() ?? Name, NameMaxLength);
        Email = Truncate(email?.Trim().ToLowerInvariant() ?? Email, EmailMaxLength);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    public static Error NotFound(string accountId) => Error.NotFound("member.notFound", $"Person '{accountId}' is not in this workspace.");
}

/// <summary>Failures when a workspace admin adds a person or sets their password.</summary>
public static class MemberErrors
{
    public static readonly Error NameRequired =
        Error.Validation("member.name", $"Enter the person's full name (up to {WorkspaceMember.NameMaxLength} characters).");

    public static readonly Error InvalidEmail = Error.Validation("member.email", "Enter a valid email address.");

    public static readonly Error WeakPassword =
        Error.Validation("member.password", "The temporary password does not meet the password rules.");

    public static readonly Error EmailTaken =
        Error.Conflict("member.emailTaken", "Someone already has an account with this email.");

    public static readonly Error AccountServiceUnavailable =
        Error.Unavailable("member.accountsUnavailable", "The sign-in service isn't answering right now. Try again in a few minutes.");
}
