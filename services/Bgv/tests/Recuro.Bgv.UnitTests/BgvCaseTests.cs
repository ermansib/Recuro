using Recuro.Bgv.Domain.Cases;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.Bgv.UnitTests;

public sealed class BgvCaseTests
{
    // Monday 5 October 2026, 09:00 UTC.
    private static readonly DateTimeOffset Monday = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly CaseActor HrTa = new("u-1", "A. Sharma", "hrta");
    private static readonly CaseActor MdCeo = new("u-9", "V. Rao", "mdceo");

    /// <summary>The Config default BGV matrix (FRD §5.5), as Config serves it.</summary>
    private static readonly CheckRule[] Matrix =
    [
        new("identity", "Identity & Address", "National ID, tax ID + 1 address proof", "All employees — always"),
        new("education", "Educational Qualification", "Recognised-bureau verification", "All roles"),
        new("employment", "Employment / Reference", "Last 2 employers", "All lateral hires"),
        new("police", "Police / Antecedent", "Customer-facing / cash-handling roles", "Flagged roles"),
        new("credit", "Credit / Bureau Check", "Finance-function roles", "Credit, Collections, Finance + Fit & Proper"),
        new("court", "Court Record / Litigation", "Risk-based", "Senior + customer-facing"),
        new("fitProper", "Fit & Proper Declaration", "Directors / KMP / Sr Mgmt only", "KMP / Senior Management"),
        new("social", "Social Media / Digital", "Optional — Sr Mgmt roles", "Senior Management (optional)"),
        new("coi", "Conflict of Interest", "Relationship with employees/Directors", "All"),
    ];

    private static RoleProfile Role(string grade, CheckScope scope = CheckScope.Full, params string[] flags) =>
        new(grade, flags.ToHashSet(StringComparer.Ordinal), scope);

    private static BgvCase NewCase(RoleProfile? role = null)
    {
        role ??= Role(Grades.M3, CheckScope.Full, RoleFlags.Finance, RoleFlags.CustomerFacing);
        return BgvCase.Initiate(
            "APP-2026-0390",
            "MRF-2026-0142",
            "c-1",
            new VendorRef("v-1", "AuthBridge"),
            "AB-88213",
            new Consent(Monday.AddDays(-1), "v1", "hr-logged"),
            role,
            "cfg-1",
            CheckPlanner.Plan(Matrix, role),
            15,
            Monday.AddDays(21),
            HrTa,
            Monday);
    }

    private static void ClearAll(BgvCase bgvCase, DateTimeOffset at)
    {
        foreach (var check in bgvCase.Checks.Where(c => c.Status == CheckStatus.Pending).ToList())
        {
            Assert.True(bgvCase.UpdateCheck(check.Type, CheckStatus.InProgress, null, null, HrTa, at).IsSuccess);
        }

        foreach (var check in bgvCase.Checks.Where(c => c.Status == CheckStatus.InProgress).ToList())
        {
            Assert.True(bgvCase.UpdateCheck(check.Type, CheckStatus.Cleared, "Verified", null, HrTa, at).IsSuccess);
        }
    }

    [Fact]
    public void The_check_transition_table_matches_the_frontend_state_machine()
    {
        // frontend/src/domain/stateMachines.ts bgvCheckTransitions, line for line.
        var expected = new Dictionary<CheckStatus, CheckStatus[]>
        {
            [CheckStatus.Pending] = [CheckStatus.InProgress, CheckStatus.NotApplicable],
            [CheckStatus.InProgress] = [CheckStatus.Cleared, CheckStatus.Flagged],
            [CheckStatus.Flagged] = [CheckStatus.Cleared],
            [CheckStatus.Cleared] = [],
            [CheckStatus.NotApplicable] = [],
        };

        foreach (var (from, targets) in expected)
        {
            Assert.Equal(targets, CheckTransitions.Table.AllowedFrom(from));
        }
    }

    [Fact]
    public void An_M3_finance_customer_facing_role_gets_the_mock_case_check_list()
    {
        // frontend/src/mocks/data/bgv-cases.json bgv-0390: Fit & Proper and Social are not applicable for M3.
        var planned = CheckPlanner.Plan(Matrix, Role(Grades.M3, CheckScope.Full, RoleFlags.Finance, RoleFlags.CustomerFacing))
            .ToDictionary(p => p.Type, p => p.Applicable);

        Assert.True(planned["identity"] && planned["education"] && planned["employment"] && planned["coi"]);
        Assert.True(planned["police"] && planned["credit"] && planned["court"]);
        Assert.False(planned["fitProper"]);
        Assert.False(planned["social"]);
    }

    [Fact]
    public void Senior_management_needs_fit_and_proper_and_credit_and_freshers_skip_employment()
    {
        var vp = CheckPlanner.Plan(Matrix, Role(Grades.Vp)).ToDictionary(p => p.Type);
        Assert.True(vp["fitProper"].Applicable);
        Assert.True(vp["credit"].Applicable);
        Assert.False(vp["police"].Applicable);
        Assert.StartsWith("Not applicable (VP)", vp["police"].NotApplicableReason, StringComparison.Ordinal);

        var fresher = CheckPlanner.Plan(Matrix, Role(Grades.E, CheckScope.Full, RoleFlags.Fresher)).ToDictionary(p => p.Type);
        Assert.False(fresher["employment"].Applicable);
        Assert.False(fresher["court"].Applicable);
    }

    [Fact]
    public void A_config_condition_overrides_the_built_in_rule_and_unknown_checks_always_apply()
    {
        CheckRule[] matrix =
        [
            new("police", "Police", "d", "c", new AppliesWhen(false, [Grades.E], [])),
            new("drugTest", "Drug test", "d", "Tenant-specific"),
        ];

        var planned = CheckPlanner.Plan(matrix, Role(Grades.E)).ToDictionary(p => p.Type, p => p.Applicable);

        Assert.True(planned["police"]);
        Assert.True(planned["drugTest"]);
    }

    [Fact]
    public void Delta_scope_skips_the_checks_already_on_file_for_internal_candidates()
    {
        var planned = CheckPlanner.Plan(Matrix, Role(Grades.M1, CheckScope.Delta)).ToDictionary(p => p.Type, p => p.Applicable);

        Assert.False(planned["identity"]);
        Assert.False(planned["education"]);
        Assert.False(planned["employment"]);
        Assert.True(planned["coi"]);
    }

    [Fact]
    public void The_gate_stays_shut_until_every_applicable_check_clears_and_then_clears_once()
    {
        var bgvCase = NewCase();
        Assert.Single(bgvCase.DomainEvents.OfType<CaseInitiatedDomainEvent>());
        var blocked = bgvCase.EnsureReleasable();
        Assert.Equal("bgv_release_blocked", blocked.Error!.Code);
        Assert.Equal(7, blocked.Error.Fields.Count);

        ClearAll(bgvCase, Monday.AddDays(3));

        Assert.Equal(CaseStatus.Cleared, bgvCase.Status);
        Assert.True(bgvCase.EnsureReleasable().IsSuccess);
        var cleared = Assert.Single(bgvCase.DomainEvents.OfType<CaseClearedDomainEvent>());
        Assert.True(cleared.OnTime);
        Assert.Equal(3, bgvCase.TatDay(Monday.AddDays(30)));
    }

    [Fact]
    public void Illegal_check_moves_are_conflicts_and_flagging_needs_an_adverse_report()
    {
        var bgvCase = NewCase();

        Assert.Equal(ErrorType.Conflict, bgvCase.UpdateCheck("identity", CheckStatus.Cleared, null, null, HrTa, Monday).Error!.Type);
        Assert.Equal("use_adverse_report", bgvCase.UpdateCheck("identity", CheckStatus.Flagged, null, null, HrTa, Monday).Error!.Fields[0].Code);
        Assert.Equal(ErrorType.NotFound, bgvCase.UpdateCheck("nope", CheckStatus.InProgress, null, null, HrTa, Monday).Error!.Type);
        Assert.Equal(ErrorType.Conflict, bgvCase.UpdateCheck("fitProper", CheckStatus.InProgress, null, null, HrTa, Monday).Error!.Type);
    }

    [Fact]
    public void An_adverse_finding_locks_release_until_an_override_clears_it()
    {
        var bgvCase = NewCase();

        Assert.True(bgvCase.ReportAdverse("court", "Pending litigation found", AdverseAction.HoldAndEscalate, HrTa, Monday).IsSuccess);

        Assert.Equal(CaseStatus.UnderReview, bgvCase.Status);
        Assert.Equal(CheckStatus.Flagged, bgvCase.Checks.Single(c => c.Type == "court").Status);
        Assert.Single(bgvCase.DomainEvents.OfType<AdverseFlaggedDomainEvent>());
        Assert.Equal("adverse_open", bgvCase.ReportAdverse("police", "Another", AdverseAction.HoldAndEscalate, HrTa, Monday).Error!.Code);
        Assert.Equal("adverse_under_review", bgvCase.UpdateCheck("court", CheckStatus.Cleared, null, null, HrTa, Monday).Error!.Code);

        Assert.Equal("reason_required", bgvCase.Resolve(AdverseOutcome.Override, " ", MdCeo, Monday).Error!.Fields[0].Code);
        Assert.True(bgvCase.Resolve(AdverseOutcome.Override, "Matter closed in 2019, documented", MdCeo, Monday.AddDays(1)).IsSuccess);

        Assert.Equal(CaseStatus.Open, bgvCase.Status);
        Assert.Equal(CheckStatus.Cleared, bgvCase.Checks.Single(c => c.Type == "court").Status);
        Assert.Equal(AdverseOutcome.Override, bgvCase.Resolution!.Outcome);
        ClearAll(bgvCase, Monday.AddDays(2));
        Assert.Equal(CaseStatus.Cleared, bgvCase.Status);
    }

    [Fact]
    public void Rescinding_closes_the_case_adverse()
    {
        var bgvCase = NewCase();
        bgvCase.ReportAdverse("credit", "Default on record", AdverseAction.SeekClarification, HrTa, Monday);

        Assert.True(bgvCase.Resolve(AdverseOutcome.Rescind, "Material default, offer withdrawn", MdCeo, Monday).IsSuccess);

        Assert.Equal(CaseStatus.Rescinded, bgvCase.Status);
        Assert.Equal("bgv_rescinded", bgvCase.EnsureReleasable().Error!.Code);
        Assert.Equal("bgv_case_closed", bgvCase.UpdateCheck("identity", CheckStatus.InProgress, null, null, HrTa, Monday).Error!.Code);
        Assert.Empty(bgvCase.DomainEvents.OfType<CaseClearedDomainEvent>());
    }

    [Fact]
    public void Cleared_and_not_applicable_checks_cannot_be_flagged()
    {
        var bgvCase = NewCase();

        Assert.Equal(ErrorType.Conflict, bgvCase.ReportAdverse("fitProper", "x", AdverseAction.HoldAndEscalate, HrTa, Monday).Error!.Type);
        Assert.Equal("description_required", bgvCase.ReportAdverse("police", " ", AdverseAction.HoldAndEscalate, HrTa, Monday).Error!.Fields[0].Code);
    }

    [Fact]
    public void A_late_clearance_is_recorded_as_not_on_time()
    {
        var bgvCase = NewCase();

        ClearAll(bgvCase, Monday.AddDays(40));

        Assert.False(Assert.Single(bgvCase.DomainEvents.OfType<CaseClearedDomainEvent>()).OnTime);
    }

    [Fact]
    public void Reassignment_moves_an_open_case_to_another_vendor()
    {
        var bgvCase = NewCase();
        bgvCase.FlagForReassignment();
        Assert.True(bgvCase.NeedsReassignment);

        Assert.True(bgvCase.Reassign(new VendorRef("v-2", "VerifyPro")).IsSuccess);

        Assert.False(bgvCase.NeedsReassignment);
        Assert.Equal("VerifyPro", bgvCase.VendorName);
        Assert.Equal(string.Empty, bgvCase.VendorCaseRef);
    }

    [Theory]
    [InlineData("2026-10-05", "2026-10-05", 0)]
    [InlineData("2026-10-05", "2026-10-09", 4)]
    [InlineData("2026-10-09", "2026-10-12", 1)]
    public void Working_days_between_skip_weekends(string from, string to, int expected) =>
        Assert.Equal(expected, WorkingDays.Between(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture)));
}
