using Recuro.Identity.Domain;
using Recuro.Identity.Domain.Access;
using Recuro.Identity.Domain.Masking;

namespace Recuro.Identity.UnitTests;

public class WorkspaceAccessTests
{
    private static readonly AccessPolicy Template = DefaultAccessPolicy.Create();

    private static WorkspaceAccess Access(IReadOnlyDictionary<string, IReadOnlyList<string>>? members = null, params WorkspaceRoleDefinition[] roles) =>
        new("roles-test", roles, members ?? new Dictionary<string, IReadOnlyList<string>>());

    private static AccessDecision Decide(AccessPolicy policy, string action, string role, string actor = "u-1", bool mfa = false) =>
        policy.Decide(new AccessRequest(actor, [role], action, [], mfa));

    [Fact]
    public void A_role_the_workspace_created_gets_exactly_its_permissions()
    {
        var policy = Access(roles: new WorkspaceRoleDefinition("recruiter", ["candidate.log", "pipeline.move"], false, true)).ApplyTo(Template);

        Assert.True(Decide(policy, "pipeline.move", "recruiter").Allow);
        Assert.True(Decide(policy, "candidate.log", "recruiter").Allow);
        Assert.False(Decide(policy, "offer.edit", "recruiter").Allow);
        Assert.Equal("rbac-2026.10+roles-test", policy.Version);
    }

    [Fact]
    public void Changing_a_persona_role_in_the_workspace_replaces_its_seeded_grants()
    {
        // This workspace's HR-TA may no longer raise requisitions; HR Head may.
        var policy = Access(roles:
        [
            new WorkspaceRoleDefinition(PersonaRoles.HrTa, ["jd.edit"], false, true),
            new WorkspaceRoleDefinition(PersonaRoles.HrHead, ["mrf.raise"], true, true),
        ]).ApplyTo(Template);

        Assert.False(Decide(policy, "mrf.raise", PersonaRoles.HrTa).Allow);
        Assert.True(Decide(policy, "mrf.raise", PersonaRoles.HrHead).Allow);
        // MD/CEO isn't defined by this workspace, so the seeded grants still apply.
        Assert.True(Decide(policy, "mrf.approve", PersonaRoles.MdCeo).Allow);
    }

    [Fact]
    public void Switched_off_roles_grant_nothing_and_roles_outside_the_workspace_keep_theirs()
    {
        var policy = Access(roles: new WorkspaceRoleDefinition(PersonaRoles.Employee, ["referral.submit"], false, IsActive: false)).ApplyTo(Template);

        Assert.False(Decide(policy, "referral.submit", PersonaRoles.Employee).Allow);
        Assert.True(Decide(policy, "careers.apply", PersonaRoles.Candidate).Allow);
    }

    [Fact]
    public void Roles_assigned_in_the_admin_portal_count_before_the_token_carries_them()
    {
        var members = new Dictionary<string, IReadOnlyList<string>> { ["u-7"] = ["recruiter", "ghost"] };
        var policy = Access(members, new WorkspaceRoleDefinition("recruiter", ["pipeline.move"], false, true)).ApplyTo(Template);

        Assert.True(Decide(policy, "pipeline.move", PersonaRoles.Employee, actor: "u-7").Allow);
        Assert.False(Decide(policy, "pipeline.move", PersonaRoles.Employee, actor: "u-8").Allow);
    }

    [Fact]
    public void Roles_marked_requires_mfa_need_step_up()
    {
        var policy = Access(roles: new WorkspaceRoleDefinition("cfo", ["offer.approve"], RequiresMfa: true, IsActive: true)).ApplyTo(Template);

        Assert.Equal([DecisionReasons.StepUpRequired], Decide(policy, "offer.approve", "cfo").Reasons);
        Assert.True(Decide(policy, "offer.approve", "cfo", mfa: true).Allow);
    }

    [Fact]
    public void Workspace_roles_are_masked_like_the_persona_with_the_same_access_to_sensitive_data()
    {
        var masking = Access(roles:
        [
            new WorkspaceRoleDefinition("recruiter", [WorkspaceAccess.SensitivePermission], false, true),
            new WorkspaceRoleDefinition("cfo", ["offer.approve"], false, true),
        ]).ApplyTo(DefaultMaskingMap.Create());

        Assert.Empty(masking.For("recruiter", "candidate")!);
        Assert.Equal(MaskStrategy.Hide, masking.For("cfo", "candidate")!["currentCtc"]);
        Assert.Null(masking.For("stranger", "candidate"));
    }
}
