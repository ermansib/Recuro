namespace Recuro.Identity.Domain.Access;

/// <summary>
/// A versioned RBAC policy: the matrix rows plus the roles that need step-up MFA on sensitive
/// actions (RCU-AUT-002/003). Pure and deterministic, so the decision can be cached and unit-tested.
/// Anything not explicitly allowed is denied.
/// </summary>
public sealed class AccessPolicy
{
    private readonly Dictionary<string, AccessRule> _rules;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _assignedRoles;

    /// <param name="version">Policy version, returned with every decision.</param>
    /// <param name="elevatedRoles">Roles that must pass MFA before a step-up action.</param>
    /// <param name="rules">The matrix rows.</param>
    /// <param name="assignedRoles">
    /// Roles the workspace gave each person (subject id → role keys). They count alongside the roles the
    /// caller sends, so a role assigned in the admin portal works before the person's token carries it.
    /// </param>
    public AccessPolicy(
        string version,
        IEnumerable<string> elevatedRoles,
        IEnumerable<AccessRule> rules,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? assignedRoles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(elevatedRoles);
        ArgumentNullException.ThrowIfNull(rules);
        Version = version;
        ElevatedRoles = elevatedRoles.ToHashSet(StringComparer.Ordinal);
        _rules = rules.ToDictionary(r => r.Action, StringComparer.Ordinal);
        _assignedRoles = assignedRoles ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    }

    public string Version { get; }

    /// <summary>Roles that must pass MFA before a step-up action (FRD: HR Head, MD/CEO, Compliance).</summary>
    public IReadOnlySet<string> ElevatedRoles { get; }

    public IEnumerable<AccessRule> Rules => _rules.Values;

    public AccessDecision Decide(AccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_rules.TryGetValue(request.Action, out var rule))
        {
            return AccessDecision.Deny(DecisionReasons.UnknownAction);
        }

        var actorRoles = ActorRolesOf(request);
        var heldRole = rule.Roles.FirstOrDefault(r => actorRoles.Contains(r));
        var roleOk = rule.Roles.Count == 0 || heldRole is not null;
        var assigned = !string.IsNullOrEmpty(request.ActorId) && request.AssigneeIds.Contains(request.ActorId, StringComparer.Ordinal);

        var reasons = new List<string>();
        switch (rule.Assignee)
        {
            case AssigneeRule.Required when !roleOk:
            case AssigneeRule.None when !roleOk:
                return AccessDecision.Deny(DecisionReasons.RoleNotPermitted);
            case AssigneeRule.Required when !assigned:
                return AccessDecision.Deny(DecisionReasons.NotAssigned);
            case AssigneeRule.RoleOrAssignee when heldRole is null && !assigned:
                return AccessDecision.Deny(DecisionReasons.RoleNotPermitted, DecisionReasons.NotAssigned);
        }

        if (heldRole is not null)
        {
            reasons.Add($"{DecisionReasons.RolePermitted}:{heldRole}");
        }

        if (assigned && rule.Assignee != AssigneeRule.None)
        {
            reasons.Add(DecisionReasons.Assigned);
        }

        if (rule.StepUp && actorRoles.Any(ElevatedRoles.Contains))
        {
            if (!request.MfaVerified)
            {
                return AccessDecision.Deny(DecisionReasons.StepUpRequired);
            }

            reasons.Add(DecisionReasons.MfaVerified);
        }

        return new AccessDecision(true, reasons);
    }

    private HashSet<string> ActorRolesOf(AccessRequest request)
    {
        var roles = request.ActorRoles.ToHashSet(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(request.ActorId) && _assignedRoles.TryGetValue(request.ActorId, out var assigned))
        {
            roles.UnionWith(assigned);
        }

        return roles;
    }
}
