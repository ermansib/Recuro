using Recuro.Admin.Domain.Roles;

namespace Recuro.Admin.UnitTests;

public class WorkspaceRoleTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static RoleChanges Changes(string name = "Recruiter", string? reportsTo = null, params string[] permissions) =>
        new(name, "Sources candidates", reportsTo, permissions, ["dashboard", "pipeline"], RequiresMfa: false);

    [Theory]
    [InlineData("recruiter")]
    [InlineData("Area-Manager")]
    [InlineData("hr2")]
    public void Tenants_can_create_roles_with_their_own_keys(string key)
    {
        var role = WorkspaceRole.Create(TenantId, key, Changes(permissions: "pipeline.move"), 10);

        Assert.True(role.IsSuccess);
        Assert.Equal(key.ToLowerInvariant(), role.Value.Key);
        Assert.False(role.Value.IsSystem);
        Assert.True(role.Value.HasPermission("pipeline.move"));
    }

    [Theory]
    [InlineData("x", "role.key")]
    [InlineData("1abc", "role.key")]
    [InlineData("has space", "role.key")]
    [InlineData("tenant-admin", "role.reservedKey")]
    [InlineData("platform-admin", "role.reservedKey")]
    [InlineData("candidate", "role.reservedKey")]
    [InlineData("default-roles-recuro", "role.reservedKey")]
    public void Keys_that_clash_with_the_platform_are_refused(string key, string code) =>
        Assert.Equal(code, WorkspaceRole.Create(TenantId, key, Changes(), 10).Error!.Code);

    [Fact]
    public void Only_catalogue_permissions_can_be_granted() =>
        Assert.Equal("role.unknownPermission", WorkspaceRole.Create(TenantId, "recruiter", Changes(permissions: "everything"), 10).Error!.Code);

    [Fact]
    public void A_role_cannot_report_to_itself() =>
        Assert.Equal("role.reportsTo", WorkspaceRole.Create(TenantId, "recruiter", Changes(reportsTo: "recruiter"), 10).Error!.Code);

    [Fact]
    public void A_switched_off_role_grants_nothing()
    {
        var role = WorkspaceRole.Create(TenantId, "recruiter", Changes(permissions: "pipeline.move"), 10).Value;

        role.SetActive(false);

        Assert.False(role.HasPermission("pipeline.move"));
    }

    [Fact]
    public void Every_workspace_starts_with_the_persona_roles()
    {
        var roles = DefaultWorkspaceRoles.CreateFor(TenantId);

        Assert.Equal(["mdceo", "hrhead", "hod", "hrta", "employee"], roles.Select(r => r.Key));
        Assert.All(roles, r => Assert.True(r.IsSystem && r.IsActive && r.TenantId == TenantId));
        Assert.All(roles.SelectMany(r => r.Permissions), p => Assert.True(PermissionCatalog.Contains(p)));
        Assert.Equal(["mdceo", "hrhead"], roles.Where(r => r.RequiresMfa).Select(r => r.Key));
    }

    [Fact]
    public void Members_can_only_be_given_active_roles_of_the_workspace()
    {
        var roles = DefaultWorkspaceRoles.CreateFor(TenantId);
        roles.Single(r => r.Key == "hod").SetActive(false);
        var member = WorkspaceMember.Create(TenantId, "kc-1", "Asha Rao", "Asha@Example.com", ["hrta"]);

        Assert.Equal("member.roles", member.Assign([], null, roles).Error!.Code);
        Assert.Equal("role.notFound", member.Assign(["ghost"], null, roles).Error!.Code);
        Assert.Equal("member.inactiveRole", member.Assign(["hod"], null, roles).Error!.Code);
        Assert.True(member.Assign(["hrta", "hrhead"], " Sales ", roles).IsSuccess);
        Assert.Equal(["hrta", "hrhead"], member.RoleKeys);
        Assert.Equal("Sales", member.Department);
        Assert.Equal("asha@example.com", member.Email);
    }
}
