namespace Recuro.Identity.Domain.Access;

/// <summary>
/// The RBAC matrix of FRD §3.2 (authoritative), seeded as data. It extends the frontend's
/// capabilities (<c>frontend/src/auth/permissions.ts</c>) with the server-only actions the backend
/// stories need. Tenants get this policy until they have their own version.
/// </summary>
public static class DefaultAccessPolicy
{
    public const string Version = "rbac-2026.10";

    private const string HrTa = PersonaRoles.HrTa;
    private const string HrHead = PersonaRoles.HrHead;
    private const string MdCeo = PersonaRoles.MdCeo;
    private const string Employee = PersonaRoles.Employee;
    private const string Candidate = PersonaRoles.Candidate;

    public static AccessPolicy Create() => new(
        Version,
        elevatedRoles: [HrHead, MdCeo],
        rules:
        [
            // Frontend capabilities, same keys and roles.
            new("mrf.raise", [HrTa]),
            new("mrf.approve", [HrHead, MdCeo]),
            new("candidate.viewSensitive", [HrTa, HrHead]),
            new("candidate.log", [HrTa]),
            new("pipeline.move", [HrTa, HrHead]),
            new("assessment.submit", [HrTa], AssigneeRule.RoleOrAssignee),
            new("bgv.initiate", [HrTa]),
            new("bgv.reportAdverse", [HrTa]),
            new("offer.edit", [HrTa]),
            new("offer.release", [HrTa, HrHead]),
            new("jd.edit", [HrTa]),
            new("approvals.view", [HrTa, HrHead, MdCeo]),
            new("internal.view", [HrTa, HrHead, MdCeo]),
            new("careers.apply", [Candidate]),
            new("team.invite", [HrTa, HrHead]),

            // Server-side rows of §3.2 that the UI has no button for yet.
            new("offer.approve", [HrHead, MdCeo], StepUp: true),
            new("bgv.decideAdverse", [HrHead, MdCeo], StepUp: true),
            new("vendor.empanel", [HrHead]),
            new("reports.view", [HrTa, HrHead, MdCeo]),
            new("ijp.apply", [Employee]),
            new("referral.submit", [Employee]),

            // RCU-WFL-002: only the current assignee decides an approval task, whatever their role.
            new("workflow.task.decide", [], AssigneeRule.Required),
        ]);
}
