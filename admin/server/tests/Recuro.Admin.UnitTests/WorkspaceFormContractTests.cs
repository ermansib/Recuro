using System.Text.Json;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.UnitTests;

/// <summary>
/// The create-workspace form is defined once in admin/contracts/workspace-form/definition.json and used by the
/// portal's sign-up page and the platform console. These tests fail when the API's rules drift from it.
/// </summary>
public class WorkspaceFormContractTests
{
    private static readonly JsonElement Form = Load();

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "contracts", "workspace-form.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static string[] Strings(string property) =>
        [.. Form.GetProperty(property).EnumerateArray().Select(e => e.GetString()!)];

    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    [Fact]
    public void Org_types_match() =>
        Assert.Equal(Strings("orgTypes"), Enum.GetNames<OrgType>().Select(CamelCase));

    [Fact]
    public void Owner_roles_match() =>
        Assert.Equal(Strings("ownerRoles"), WorkspaceRoles.OwnerRoles);

    [Fact]
    public void Each_org_type_starts_with_the_same_owner_role()
    {
        foreach (var orgType in Enum.GetValues<OrgType>())
        {
            var expected = Form.GetProperty("defaultOwnerRole").GetProperty(CamelCase(orgType.ToString())).GetString();
            Assert.Equal(expected, OrgTypeDefaults.For(orgType).OwnerRole);
        }

        Assert.Equal("smallBusiness", Form.GetProperty("defaultOrgType").GetString());
    }

    [Fact]
    public void Limits_match()
    {
        Assert.Equal(WorkspaceForm.OrgNameMaxLength, Form.GetProperty("orgNameMaxLength").GetInt32());
        Assert.Equal(WorkspaceForm.OwnerNameMaxLength, Form.GetProperty("adminNameMaxLength").GetInt32());
        Assert.Equal(WorkspaceForm.SlugMaxLength, Form.GetProperty("slugMaxLength").GetInt32());
        Assert.Equal(PasswordPolicy.MinLength, Form.GetProperty("passwordMinLength").GetInt32());
    }

    [Fact]
    public void Fields_are_the_ones_both_requests_carry() =>
        Assert.Equal(["orgType", "orgName", "adminName", "adminRole", "email", "password"], Strings("fields"));
}
