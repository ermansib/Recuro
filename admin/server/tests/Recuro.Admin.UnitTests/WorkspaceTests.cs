using Recuro.Admin.Application.Workspaces;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.UnitTests;

public class WorkspaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Aurora Housing Finance", "aurora-housing-finance")]
    [InlineData("  Ümlaut & Co. ", "umlaut-co")]
    [InlineData("!!!", "workspace")]
    [InlineData("A very long organisation name that goes on and on forever", "a-very-long-organisation-name-that-goes")]
    public void Slugs_follow_the_frontend_rules(string name, string expected) =>
        Assert.Equal(expected, WorkspaceSlug.From(name));

    [Theory]
    [InlineData("Str0ng!pass", true)]
    [InlineData("Sh0rt!", false)]
    [InlineData("nouppercase1!", false)]
    [InlineData("NoDigits!!", false)]
    [InlineData("NoSymbol123", false)]
    public void Password_policy_matches_the_sign_up_form(string password, bool ok) =>
        Assert.Equal(ok, PasswordPolicy.IsSatisfiedBy(password));

    [Fact]
    public void A_signed_up_workspace_starts_on_starter_with_its_org_type_defaults()
    {
        var tenant = Tenant.SignUp("Globex", "globex", OrgType.Enterprise, " Globex.Example ", "en-GB", "gbp", Now).Value;

        Assert.Equal((TenantKind.InHouse, TenantPlan.Starter, TenantStatus.Active), (tenant.Kind, tenant.Plan, tenant.Status));
        Assert.Equal(["microsoft", "google", "saml"], tenant.SsoProviders);
        Assert.Equal(["hrhead", "mdceo"], tenant.MfaRoles);
        Assert.Equal(("globex.example", "en-GB", "GBP"), (tenant.EmailDomain, tenant.Locale, tenant.Currency));
        Assert.Equal("Grow your career where it matters", tenant.CareersTagline);
    }
}
