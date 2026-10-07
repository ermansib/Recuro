namespace Recuro.Identity.Domain.Access;

/// <summary>How being assigned to the record (an interviewer, the current approver) affects a rule.</summary>
public enum AssigneeRule
{
    /// <summary>Only roles count.</summary>
    None,

    /// <summary>The actor must hold a listed role (any role when none are listed) and be assigned.</summary>
    Required,

    /// <summary>A listed role or being assigned is enough (FRD §3.2: "assigned interviewer only; HR-TA has admin view").</summary>
    RoleOrAssignee,
}

/// <summary>
/// One row of the RBAC matrix (FRD §3.2) as data. <paramref name="Action"/> uses the frontend's
/// capability keys (<c>frontend/src/auth/permissions.ts</c>) so both sides speak the same names.
/// </summary>
/// <param name="Action">Capability key, e.g. <c>offer.approve</c>.</param>
/// <param name="Roles">Roles allowed to perform it.</param>
/// <param name="Assignee">Whether assignment to the record is needed or enough.</param>
/// <param name="StepUp">Sensitive action: elevated roles must have passed MFA (RCU-AUT-002).</param>
public sealed record AccessRule(string Action, IReadOnlyList<string> Roles, AssigneeRule Assignee = AssigneeRule.None, bool StepUp = false);

/// <summary>Who is asking, to do what, on which record (the body of <c>POST /decide</c>, RCU-AUT-003).</summary>
/// <param name="ActorId">Subject id of the person.</param>
/// <param name="ActorRoles">The person's roles.</param>
/// <param name="Action">Capability key.</param>
/// <param name="AssigneeIds">Subject ids assigned to the record, when assignment matters.</param>
/// <param name="MfaVerified">Whether the person's session passed a second factor (token <c>amr</c> / <c>acr</c>).</param>
public sealed record AccessRequest(
    string ActorId,
    IReadOnlyList<string> ActorRoles,
    string Action,
    IReadOnlyList<string> AssigneeIds,
    bool MfaVerified);

/// <summary>Allow or deny, with machine-readable reasons for the caller's logs and audit.</summary>
public sealed record AccessDecision(bool Allow, IReadOnlyList<string> Reasons)
{
    public static AccessDecision Deny(params string[] reasons) => new(false, reasons);

    public static AccessDecision Permit(params string[] reasons) => new(true, reasons);
}

/// <summary>Reason codes in <see cref="AccessDecision.Reasons"/>.</summary>
public static class DecisionReasons
{
    public const string UnknownAction = "unknown_action";
    public const string RoleNotPermitted = "role_not_permitted";
    public const string NotAssigned = "not_assigned";
    public const string StepUpRequired = "step_up_required";
    public const string RolePermitted = "role_permitted";
    public const string Assigned = "assigned";
    public const string MfaVerified = "mfa_verified";
}
