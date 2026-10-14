using Recuro.Identity.Domain.Masking;

namespace Recuro.Identity.Domain.Access;

/// <summary>A role a workspace defined in the admin portal: its permission keys and whether holders need MFA.</summary>
public sealed record WorkspaceRoleDefinition(string Key, IReadOnlyList<string> Permissions, bool RequiresMfa, bool IsActive);

/// <summary>
/// A workspace's own roles and who holds them, as configured in the admin portal (the admin API's
/// <c>GET /api/v1/internal/workspaces/{id}/access</c>). Applied on top of the seeded FRD §3.2 policy, so the
/// policy decision point and masking follow the workspace's data instead of fixed role constants.
/// </summary>
public sealed record WorkspaceAccess(
    string Version,
    IReadOnlyList<WorkspaceRoleDefinition> Roles,
    IReadOnlyDictionary<string, IReadOnlyList<string>> MemberRoles)
{
    /// <summary>The permission that decides whether a role sees candidates' personal data unmasked.</summary>
    public const string SensitivePermission = "candidate.viewSensitive";

    /// <summary>
    /// Stands in for "no role" on a rule the workspace left without holders: an empty role list would mean
    /// "any role". Role keys can't contain brackets, so nobody holds it.
    /// </summary>
    public const string NoRole = "(none)";

    /// <summary>
    /// The workspace's policy: for every action of the template, the roles the workspace defines are replaced by
    /// its active roles holding that permission. Roles the workspace does not define (candidate, service) keep
    /// the template's grants, and so do actions that depend only on assignment. Switched-off roles grant nothing.
    /// </summary>
    public AccessPolicy ApplyTo(AccessPolicy template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var defined = Roles.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        var active = Roles.Where(r => r.IsActive).ToList();

        var rules = template.Rules.Select(rule => rule.Roles.Count == 0 ? rule : rule with { Roles = HoldersOf(rule, defined, active) });
        var elevated = template.ElevatedRoles.Where(r => !defined.Contains(r)).Concat(active.Where(r => r.RequiresMfa).Select(r => r.Key));

        var activeKeys = active.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        var assigned = MemberRoles.ToDictionary(
            m => m.Key,
            m => (IReadOnlyList<string>)m.Value.Where(activeKeys.Contains).ToList(),
            StringComparer.Ordinal);

        return new AccessPolicy($"{template.Version}+{Version}", elevated, rules, assigned);
    }

    private static List<string> HoldersOf(AccessRule rule, HashSet<string> defined, List<WorkspaceRoleDefinition> active)
    {
        var holders = rule.Roles.Where(r => !defined.Contains(r))
            .Concat(active.Where(r => r.Permissions.Contains(rule.Action, StringComparer.Ordinal)).Select(r => r.Key))
            .ToList();
        return holders.Count == 0 ? [NoRole] : holders;
    }

    /// <summary>
    /// The masking map with each workspace-defined role mapped onto a persona's map: roles allowed to see
    /// sensitive candidate data mask like HR-TA, every other role like MD/CEO (the strictest staff map).
    /// </summary>
    public MaskingMap ApplyTo(MaskingMap template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var aliases = Roles
            .Where(r => r.IsActive && !PersonaRoles.HasMaskingMap(r.Key))
            .ToDictionary(
                r => r.Key,
                r => r.Permissions.Contains(SensitivePermission, StringComparer.Ordinal) ? PersonaRoles.HrTa : PersonaRoles.MdCeo,
                StringComparer.Ordinal);
        return template.WithAliases($"{template.Version}+{Version}", aliases);
    }
}
