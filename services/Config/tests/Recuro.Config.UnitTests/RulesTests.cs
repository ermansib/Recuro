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
    public void Every_seed_matrix_passes_its_own_validation()
    {
        foreach (var (type, matrix) in DefaultRuleSets.All)
        {
            var element = JsonSerializer.SerializeToElement(matrix, matrix.GetType(), MatrixJson.Options);
            var result = new ProposeVersionCommandValidator().Validate(new ProposeVersionCommand(type, DateTimeOffset.UtcNow, element, null));

            Assert.True(result.IsValid, $"{type}: {string.Join("; ", result.Errors.Select(e => $"{e.PropertyName} {e.ErrorMessage}"))}");
        }
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
