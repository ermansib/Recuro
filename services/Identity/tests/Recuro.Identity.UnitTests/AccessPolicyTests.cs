using Recuro.Identity.Domain;
using Recuro.Identity.Domain.Access;

namespace Recuro.Identity.UnitTests;

public class AccessPolicyTests
{
    private static readonly AccessPolicy Policy = DefaultAccessPolicy.Create();

    private static AccessDecision Decide(string action, string role, bool mfa = false, string actor = "u-1", params string[] assignees) =>
        Policy.Decide(new AccessRequest(actor, [role], action, assignees, mfa));

    [Theory]
    [InlineData("mrf.raise", PersonaRoles.HrTa, true)]
    [InlineData("mrf.raise", PersonaRoles.HrHead, false)]
    [InlineData("mrf.approve", PersonaRoles.HrHead, true)]
    [InlineData("mrf.approve", PersonaRoles.HrTa, false)]
    [InlineData("candidate.viewSensitive", PersonaRoles.MdCeo, false)]
    [InlineData("pipeline.move", PersonaRoles.MdCeo, false)]
    [InlineData("pipeline.move", PersonaRoles.HrHead, true)]
    [InlineData("bgv.initiate", PersonaRoles.HrTa, true)]
    [InlineData("careers.apply", PersonaRoles.Candidate, true)]
    [InlineData("careers.apply", PersonaRoles.Employee, false)]
    [InlineData("referral.submit", PersonaRoles.Employee, true)]
    [InlineData("vendor.empanel", PersonaRoles.HrTa, false)]
    public void The_FRD_matrix_decides_by_role(string action, string role, bool allowed) =>
        Assert.Equal(allowed, Decide(action, role).Allow);

    [Fact]
    public void Unknown_actions_are_denied_by_default()
    {
        var decision = Decide("payroll.run", PersonaRoles.HrHead);

        Assert.False(decision.Allow);
        Assert.Equal([DecisionReasons.UnknownAction], decision.Reasons);
    }

    [Fact]
    public void A_permitted_role_is_named_in_the_reasons() =>
        Assert.Equal(["role_permitted:hrta"], Decide("jd.edit", PersonaRoles.HrTa).Reasons);

    [Fact]
    public void Elevated_roles_need_MFA_for_sensitive_actions()
    {
        var withoutMfa = Decide("offer.approve", PersonaRoles.MdCeo);
        var withMfa = Decide("offer.approve", PersonaRoles.MdCeo, mfa: true);

        Assert.False(withoutMfa.Allow);
        Assert.Equal([DecisionReasons.StepUpRequired], withoutMfa.Reasons);
        Assert.True(withMfa.Allow);
        Assert.Contains(DecisionReasons.MfaVerified, withMfa.Reasons);
    }

    [Fact]
    public void Only_the_assignee_decides_a_workflow_task()
    {
        Assert.True(Decide("workflow.task.decide", PersonaRoles.HrHead, actor: "u-7", assignees: "u-7").Allow);

        var other = Decide("workflow.task.decide", PersonaRoles.HrHead, actor: "u-8", assignees: "u-7");
        Assert.False(other.Allow);
        Assert.Equal([DecisionReasons.NotAssigned], other.Reasons);
    }

    [Fact]
    public void Assessment_is_open_to_HR_TA_or_the_assigned_interviewer()
    {
        Assert.True(Decide("assessment.submit", PersonaRoles.HrTa).Allow);
        Assert.True(Decide("assessment.submit", PersonaRoles.Employee, actor: "panel-1", assignees: "panel-1").Allow);
        Assert.False(Decide("assessment.submit", PersonaRoles.Employee, actor: "panel-2", assignees: "panel-1").Allow);
    }

    [Fact]
    public void Every_frontend_capability_has_a_rule()
    {
        string[] frontend =
        [
            "mrf.raise", "mrf.approve", "candidate.viewSensitive", "candidate.log", "pipeline.move", "assessment.submit",
            "bgv.initiate", "bgv.reportAdverse", "offer.edit", "offer.release", "jd.edit", "approvals.view",
            "internal.view", "careers.apply", "team.invite",
        ];

        Assert.All(frontend, action => Assert.Contains(Policy.Rules, r => r.Action == action));
    }
}
