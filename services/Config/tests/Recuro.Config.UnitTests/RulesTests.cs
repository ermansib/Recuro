using System.Text.Json;
using Recuro.Config.Application.RuleSets;
using Recuro.Config.Application.RuleSets.Commands;
using Recuro.Config.Domain.Rules;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.UnitTests;

public class RulesTests
{
    private static readonly BusinessCalendar Calendar = new("default", [DayOfWeek.Saturday, DayOfWeek.Sunday], [new(2026, 10, 2)]);

    [Theory]
    [InlineData("2026-09-30", 0, "2026-09-30")]
    [InlineData("2026-09-30", 1, "2026-10-01")]
    // Fri 2 Oct is a holiday and 3–4 Oct a weekend.
    [InlineData("2026-09-30", 2, "2026-10-05")]
    [InlineData("2026-09-30", 3, "2026-10-06")]
    // Starting on a Saturday: the next working day is Monday.
    [InlineData("2026-10-10", 1, "2026-10-12")]
    // Backwards: Tue 6 Oct − 3 working days skips the weekend and the Fri 2 Oct holiday.
    [InlineData("2026-10-06", -1, "2026-10-05")]
    [InlineData("2026-10-06", -2, "2026-10-01")]
    [InlineData("2026-10-06", -3, "2026-09-30")]
    // Starting on a Sunday: the previous working day is Friday.
    [InlineData("2026-10-11", -1, "2026-10-09")]
    public void Working_days_skip_weekends_and_holidays(string from, int days, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Calendar.AddWorkingDays(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), days));

    [Fact]
    public void A_calendar_without_working_days_is_refused()
    {
        var never = new BusinessCalendar("x", Enum.GetValues<DayOfWeek>(), []);

        Assert.Throws<InvalidOperationException>(() => never.AddWorkingDays(new DateOnly(2026, 1, 1), 1));
    }

    [Fact]
    public void Out_of_budget_routes_gain_the_extra_leg()
    {
        var m3 = DefaultRuleSets.Doa.Find("m3")!;

        var inBudget = DoaMatrix.LegsFor(m3, BudgetStatus.In);
        var oob = DoaMatrix.LegsFor(m3, BudgetStatus.Oob);

        Assert.Equal(["Recommend", "Approve"], inBudget.Select(l => l.Name));
        Assert.Equal(inBudget.Count + 1, oob.Count);
        Assert.Equal("mdceo", oob[^1].Assignees[0].Role);
    }

    [Fact]
    public void The_seed_matches_the_frontend_rules_for_DOA()
    {
        var vp = DefaultRuleSets.Doa.Find("VP")!;

        Assert.Equal(["E", "M1", "M3", "VP", "KMP"], DefaultRuleSets.Doa.Routes.Select(r => r.Grade));
        Assert.Equal("mdceo", vp.ApproverRole);
        Assert.Equal(new TatRange(25, 35, "25–35 wd (managerial)"), vp.OverallTat);
    }

    [Fact]
    public void An_offer_matrix_stored_before_the_offer_policy_fields_reads_with_the_defaults()
    {
        var content = JsonDocument.Parse("""{"rules":[{"levels":["E"],"withinBand":{"label":"a","approverRole":"hrhead"},"deviation":{"label":"b","approverRole":"mdceo"}}]}""").RootElement;

        Assert.True(MatrixJson.TryParse(MatrixType.Offer, content, out var parsed, out var error), error);
        var offer = Assert.IsType<OfferMatrix>(parsed);
        Assert.Null(offer.CtcRules);
        Assert.Equal((5, 3, 7), (offer.ValidityWorkingDays, offer.FirstChaseAfterWorkingDays, offer.ChaseEveryDays));
    }

    [Fact]
    public void CTC_rules_need_a_sensible_range()
    {
        var bad = DefaultRuleSets.Offer with { CtcRules = [new CtcRule("basic", "Basic", 60, 40)] };
        var element = JsonSerializer.SerializeToElement(bad, MatrixJson.Options);

        var result = new ProposeVersionCommandValidator().Validate(new ProposeVersionCommand(MatrixType.Offer, DateTimeOffset.UtcNow, element, null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void The_onboarding_seed_follows_Annexure_E_and_is_valid()
    {
        var onboarding = DefaultRuleSets.Onboarding;

        Assert.Equal(11, onboarding.Checklist.Count);
        Assert.Equal(6, onboarding.Documents.Count(d => d.Mandatory));
        Assert.Equal([21, 7], onboarding.EngagementDaysBefore);
        Assert.Equal((5, 6, 30, 60, 90), (onboarding.ProvisioningWorkingDaysBefore, onboarding.ProbationMonths, onboarding.CheckInDay, onboarding.ReviewDay, onboarding.ReviewWindowEndDay));
        Assert.True(MatrixValidators.Validate(MatrixType.Onboarding, onboarding).IsValid);
    }

    [Fact]
    public void An_onboarding_review_before_the_check_in_is_refused()
    {
        var invalid = DefaultRuleSets.Onboarding with { CheckInDay = 60, ReviewDay = 30 };

        Assert.False(MatrixValidators.Validate(MatrixType.Onboarding, invalid).IsValid);
    }

    [Fact]
    public void The_interview_seed_follows_the_FRD()
    {
        var interview = DefaultRuleSets.Interview;

        Assert.Equal(["hr-screen", "functional", "business", "final"], interview.Templates.Single(t => t.Grade == "KMP").Rounds.Select(r => r.Type));
        Assert.Equal(new FeedbackPolicy(24, 48), interview.Feedback);
        Assert.Equal(["M3", "VP", "KMP"], interview.Ratification.Grades);
        Assert.Equal("hrhead", interview.Ratification.Role);
    }

    [Fact]
    public void Every_seed_matrix_passes_its_own_validation()
    {
        foreach (var (type, matrix) in DefaultRuleSets.All)
        {
            var element = JsonSerializer.SerializeToElement(matrix, matrix.GetType(), MatrixJson.Options);
            var result = new ProposeVersionCommandValidator().Validate(new ProposeVersionCommand(type, DateTimeOffset.UtcNow, element, null));

            Assert.True(result.IsValid, $"{type}: {string.Join("; ", result.Errors.Select(e => $"{e.PropertyName} {e.ErrorMessage}"))}");
        }
    }

    [Theory]
    [InlineData("""{"always":true}""", true)]
    [InlineData("""{"grades":["VP","KMP"],"anyFlags":["customerFacing"]}""", true)]
    [InlineData("""{"always":true,"grades":["VP"]}""", false)]
    [InlineData("""{}""", false)]
    [InlineData("""{"anyFlags":["Customer facing"]}""", false)]
    public void A_BGV_check_can_say_when_it_applies(string appliesWhen, bool valid)
    {
        var content = JsonDocument.Parse($$"""{"checks":[{"type":"credit","label":"Credit","detail":"x","condition":"Finance","appliesWhen":{{appliesWhen}}}]}""").RootElement;

        var result = new ProposeVersionCommandValidator().Validate(new ProposeVersionCommand(MatrixType.Bgv, DateTimeOffset.UtcNow, content, null));

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Unknown_properties_are_refused_rather_than_ignored()
    {
        var content = JsonDocument.Parse("""{"checks":[{"type":"identity","label":"ID","detail":"x","condition":"All","mandatory":true}]}""").RootElement;

        Assert.False(MatrixJson.TryParse(MatrixType.Bgv, content, out _, out var error));
        Assert.Contains("mandatory", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Business_errors_name_the_field_in_the_clients_casing()
    {
        var bad = DefaultRuleSets.Doa with { Routes = [DefaultRuleSets.Doa.Routes[0] with { ApproverRole = "boss" }] };
        var element = JsonSerializer.SerializeToElement(bad, MatrixJson.Options);

        var result = new ProposeVersionCommandValidator().Validate(new ProposeVersionCommand(MatrixType.Doa, DateTimeOffset.UtcNow, element, null));

        Assert.Contains(result.Errors, e => e.PropertyName == "content.routes[0].approverRole");
    }

    [Fact]
    public void A_missing_required_value_is_a_shape_error()
    {
        var content = JsonDocument.Parse("""{"defaultLocation":"default"}""").RootElement;

        Assert.False(MatrixJson.TryParse(MatrixType.Calendar, content, out _, out _));
    }
}
