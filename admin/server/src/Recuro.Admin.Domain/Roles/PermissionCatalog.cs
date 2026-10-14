namespace Recuro.Admin.Domain.Roles;

/// <summary>One action a role can be allowed to take, with the module it belongs to.</summary>
public sealed record PermissionDefinition(string Key, string Module, string Label);

/// <summary>
/// Product catalogue of the permissions a workspace role can hold. Keys are the RBAC capability keys used by
/// the Identity service's policy (services/Identity DefaultAccessPolicy) and the main portal
/// (frontend/src/auth/permissions.ts), so every side speaks the same names. Tenants choose which roles get
/// which permission; they cannot invent new permissions, because code has to enforce each one.
/// </summary>
public static class PermissionCatalog
{
    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new("mrf.raise", "Requisitions", "Raise manpower requisitions"),
        new("mrf.approve", "Requisitions", "Approve manpower requisitions"),
        new("approvals.view", "Requisitions", "See the approvals inbox"),
        new("jd.edit", "Job descriptions", "Write and edit job descriptions"),
        new("candidate.log", "Candidates", "Add candidates"),
        new("candidate.viewSensitive", "Candidates", "See candidate contact details and CTC"),
        new("pipeline.move", "Candidates", "Move candidates between stages"),
        new("assessment.submit", "Interviews", "Submit interview assessments"),
        new("bgv.initiate", "Background verification", "Start background checks"),
        new("bgv.reportAdverse", "Background verification", "Report adverse check results"),
        new("bgv.decideAdverse", "Background verification", "Decide on adverse check results"),
        new("offer.edit", "Offers", "Draft and edit offers"),
        new("offer.approve", "Offers", "Approve offers"),
        new("offer.release", "Offers", "Send approved offers"),
        new("vendor.empanel", "Vendors", "Empanel vendors and consultants"),
        new("reports.view", "Reports", "See reports and KPIs"),
        new("internal.view", "Employee portal", "See internal job postings as HR"),
        new("ijp.apply", "Employee portal", "Apply to internal job postings"),
        new("referral.submit", "Employee portal", "Refer candidates"),
        new("team.invite", "Workspace", "Invite colleagues to the workspace"),
    ];

    private static readonly HashSet<string> Keys = All.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

    public static bool Contains(string key) => Keys.Contains(key);
}
