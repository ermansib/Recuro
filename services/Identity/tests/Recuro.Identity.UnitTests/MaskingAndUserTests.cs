using Recuro.Identity.Application.Users;
using Recuro.Identity.Domain;
using Recuro.Identity.Domain.Masking;
using Recuro.Identity.Domain.Users;

namespace Recuro.Identity.UnitTests;

public class MaskingAndUserTests
{
    private static readonly Guid Tenant = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01");
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MD_CEO_sees_candidate_CTC_hidden_and_contact_partial()
    {
        var fields = DefaultMaskingMap.Create().For(PersonaRoles.MdCeo, "candidate")!;

        Assert.Equal(MaskStrategy.Hide, fields["currentCtc"]);
        Assert.Equal(MaskStrategy.Hide, fields["expectedCtc"]);
        Assert.Equal(MaskStrategy.Partial, fields["phone"]);
    }

    [Theory]
    [InlineData(PersonaRoles.HrTa)]
    [InlineData(PersonaRoles.HrHead)]
    public void HR_sees_every_field(string role) => Assert.Empty(DefaultMaskingMap.Create().For(role, "candidate")!);

    [Theory]
    [InlineData("tenant-admin", "candidate")]
    [InlineData(PersonaRoles.HrTa, "payslip")]
    public void Unknown_roles_and_resources_have_no_map(string role, string resource) =>
        Assert.Null(DefaultMaskingMap.Create().For(role, resource));

    [Fact]
    public void Service_accounts_see_candidate_name_and_email_only()
    {
        var fields = DefaultMaskingMap.Create().For(PersonaRoles.Service, "candidate")!;

        Assert.False(fields.ContainsKey("name"));
        Assert.False(fields.ContainsKey("email"));
        Assert.Equal(MaskStrategy.Hide, fields["phone"]);
        Assert.Equal(MaskStrategy.Hide, fields["summary"]);
        Assert.Equal(MaskStrategy.Hide, fields["currentCtc"]);
        Assert.Equal(MaskStrategy.Hide, fields["expectedCtc"]);
    }

    [Theory]
    [InlineData(PersonaRoles.MdCeo, true)]
    [InlineData(PersonaRoles.Employee, true)]
    [InlineData(PersonaRoles.Candidate, true)]
    [InlineData(PersonaRoles.Service, true)]
    [InlineData(PersonaRoles.HrTa, false)]
    [InlineData(PersonaRoles.HrHead, false)]
    public void Only_HR_sees_a_BGV_checks_sensitive_note(string role, bool hidden)
    {
        var fields = DefaultMaskingMap.Create().For(role, "bgvCheck")!;

        Assert.Equal(hidden, fields.TryGetValue("sensitiveNote", out var strategy) && strategy == MaskStrategy.Hide);
    }

    [Fact]
    public void Offers_hide_the_CTC_breakup_and_band_from_MD_CEO_and_services_and_everything_from_outsiders()
    {
        var map = DefaultMaskingMap.Create();

        Assert.Equal(["band", "components"], map.For(PersonaRoles.MdCeo, "offer")!.Keys.Order());
        Assert.Equal(["band", "components"], map.For(PersonaRoles.Service, "offer")!.Keys.Order());
        Assert.Equal(["band", "candidateName", "components"], map.For(PersonaRoles.Employee, "offer")!.Keys.Order());
        Assert.Empty(map.For(PersonaRoles.HrHead, "offer")!);
    }

    [Theory]
    [InlineData(PersonaRoles.HrTa, "sourceCost")]
    [InlineData(PersonaRoles.HrHead, null)]
    [InlineData(PersonaRoles.MdCeo, null)]
    [InlineData(PersonaRoles.Service, null)]
    public void Reports_hide_channel_spend_from_HR_TA_only(string role, string? hidden)
    {
        var fields = DefaultMaskingMap.Create().For(role, "report")!;

        Assert.Equal(hidden is null ? [] : [hidden], fields.Keys);
    }

    [Theory]
    [InlineData(PersonaRoles.Employee)]
    [InlineData(PersonaRoles.Candidate)]
    public void Roles_not_listed_on_reports_have_no_map(string role) => Assert.Null(DefaultMaskingMap.Create().For(role, "report"));

    [Fact]
    public void Service_accounts_are_never_mirrored_as_users() => Assert.False(PersonaRoles.IsMirrored(PersonaRoles.Service));

    [Fact]
    public void Provisioning_keeps_only_Recuro_roles_and_raises_an_event()
    {
        var user = UserAccount.Provision(Tenant, "sub-1", "Kavya Iyer", "k@aurora.example", ["offline_access", "hrhead", "default-roles-recuro", "hrta"], Now);

        Assert.Equal(["hrhead", "hrta"], user.Roles);
        var provisioned = Assert.IsType<UserProvisionedDomainEvent>(Assert.Single(user.DomainEvents));
        Assert.Equal("sub-1", provisioned.Subject);
    }

    [Fact]
    public void A_role_change_raises_an_event_and_an_unchanged_sync_does_not()
    {
        var user = UserAccount.Provision(Tenant, "sub-1", "Kavya Iyer", "k@aurora.example", ["hrta"], Now);
        user.ClearDomainEvents();

        user.SyncFromToken("Kavya Iyer", "k@aurora.example", ["hrta"], Now.AddHours(1));
        Assert.Empty(user.DomainEvents);

        user.SyncFromToken("Kavya Iyer", "k@aurora.example", ["hrhead"], Now.AddHours(2));
        var changed = Assert.IsType<UserRolesChangedDomainEvent>(Assert.Single(user.DomainEvents));
        Assert.Equal(["hrta"], changed.From);
        Assert.Equal(["hrhead"], changed.To);
    }

    [Fact]
    public void Heads_of_department_are_mirrored_and_the_placement_follows_the_token()
    {
        var user = UserAccount.Provision(Tenant, "sub-1", "Ravi Menon", "r@x", ["employee", "hod"], Now, UserPlacement.From(" Operations ", "sub-ceo"));

        Assert.Equal(["employee", "hod"], user.Roles);
        Assert.Equal(("operations", "sub-ceo"), (user.Department, user.ManagerId));

        user.SyncFromToken("Ravi Menon", "r@x", ["employee", "hod"], Now.AddHours(1), UserPlacement.From(null, null));
        Assert.Equal((string.Empty, string.Empty), (user.Department, user.ManagerId));
    }

    [Theory]
    [InlineData("credit-ops", "credit-ops")]
    [InlineData("Credit-Ops", "credit-ops")]
    [InlineData("credit ops", "")]
    [InlineData("1st-floor", "")]
    public void Department_keys_are_lowercase_keys_or_dropped(string raw, string expected) =>
        Assert.Equal(expected, UserPlacement.From(raw, null).Department);

    [Fact]
    public void The_user_DTO_uses_the_most_senior_persona_and_initials()
    {
        var dto = UserDto.From(UserAccount.Provision(Tenant, "sub-9", "kavya  iyer rao", "k@x", ["hrta", "mdceo"], Now));

        Assert.Equal("mdceo", dto.Role);
        Assert.Equal("KI", dto.Initials);
        Assert.Equal("sub-9", dto.Id);
        Assert.Equal(Tenant.ToString(), dto.TenantId);
    }
}
