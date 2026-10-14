namespace Recuro.Admin.Domain.Roles;

/// <summary>A role every new workspace starts with. Tenants can rename, change or switch these off later.</summary>
public sealed record DefaultRoleSpec(
    string Key,
    string Name,
    string Description,
    string? ReportsTo,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Pages,
    bool RequiresMfa);

/// <summary>
/// The persona roles of FRD §3.2 (HR-TA, HR Head, HOD, MD/CEO, Employee) as starting data. Their keys are also
/// Keycloak realm roles, so workspaces created before roles were configurable keep working unchanged.
/// </summary>
public static class DefaultWorkspaceRoles
{
    public const string HrTa = "hrta";
    public const string HrHead = "hrhead";
    public const string Hod = "hod";
    public const string MdCeo = "mdceo";
    public const string Employee = "employee";

    private static readonly string[] StaffPages =
        ["dashboard", "mrf", "approvals", "jd", "pipeline", "assessment", "bgv", "offer", "onboarding", "vendors", "reports"];

    public static readonly IReadOnlyList<DefaultRoleSpec> All =
    [
        new(MdCeo, "MD/CEO", "Final approver for senior hires and offers. Sees the board view; CTC and personal data are masked.", null,
            ["mrf.approve", "approvals.view", "offer.approve", "bgv.decideAdverse", "reports.view", "internal.view"],
            ["dashboard", "approvals", "reports"],
            RequiresMfa: true),
        new(HrHead, "HR Head", "Owns hiring policy: approves requisitions and offers, empanels vendors.", MdCeo,
            ["mrf.approve", "approvals.view", "candidate.viewSensitive", "pipeline.move", "offer.approve", "offer.release",
             "bgv.decideAdverse", "vendor.empanel", "reports.view", "internal.view", "team.invite"],
            StaffPages,
            RequiresMfa: true),
        new(Hod, "Head of Department", "Approves hiring and probation decisions for their own department.", HrHead,
            ["mrf.approve", "approvals.view", "reports.view"],
            ["dashboard", "approvals", "onboarding", "reports"],
            RequiresMfa: false),
        new(HrTa, "HR-TA", "Talent acquisition: raises requisitions, sources candidates and runs the pipeline to offer.", HrHead,
            ["mrf.raise", "approvals.view", "jd.edit", "candidate.log", "candidate.viewSensitive", "pipeline.move", "assessment.submit",
             "bgv.initiate", "bgv.reportAdverse", "offer.edit", "offer.release", "reports.view", "internal.view", "team.invite"],
            StaffPages,
            RequiresMfa: false),
        new(Employee, "Employee", "Staff using the employee portal for internal jobs and referrals.", null,
            ["ijp.apply", "referral.submit"],
            ["internalCareers", "careers"],
            RequiresMfa: false),
    ];

    /// <summary>The default roles as rows for a new workspace.</summary>
    public static IReadOnlyList<WorkspaceRole> CreateFor(Guid tenantId) =>
        All.Select((spec, index) => WorkspaceRole.FromDefault(tenantId, spec, (index + 1) * 10)).ToList();
}
