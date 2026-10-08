using Recuro.Requisition.Domain.JobDescriptions;

namespace Recuro.Requisition.UnitTests;

public class JobDescriptionTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static JobDescription Draft() => JobDescription.CreateFromRequisition(
        Guid.NewGuid(),
        "REQ-2026-0001",
        new JobDescriptionContent("", [], "N. Sharma", "", "Mumbai", "CA", "", "M3", [], [], ""),
        "A. Sharma",
        Today);

    private static JobDescriptionContent Valid(params string[] responsibilities) => new(
        "Own credit appraisal.",
        responsibilities.Length == 0 ? ["Appraise proposals", "Recommend sanctions", "  "] : responsibilities,
        "N. Sharma",
        "None",
        "Mumbai",
        "CA",
        "6–9 years",
        "M3",
        ["domain", "analytical", "integrity"],
        ["Case study"],
        "≥ 65%");

    [Fact]
    public void Submitting_freezes_a_new_version_and_records_it()
    {
        var jd = Draft();

        var result = jd.Submit(Valid(), "A. Sharma", Today);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, jd.VersionNumber);
        Assert.Equal(JobDescriptionStatus.Submitted, jd.Status);
        Assert.Equal([2, 1], jd.History.Select(h => h.Version));
        Assert.Equal(["Appraise proposals", "Recommend sanctions"], jd.Content.Responsibilities);
    }

    [Fact]
    public void The_builder_rules_are_enforced()
    {
        var tooFew = Valid("Only one") with { Purpose = " ", Competencies = ["domain"], Assessments = [] };

        var result = Draft().Submit(tooFew, "A. Sharma", Today);

        Assert.Equal(["purpose", "responsibilities", "competencies", "assessments"], result.Error!.Fields.Select(f => f.Field));
    }

    [Fact]
    public void At_most_six_responsibilities()
    {
        var result = Draft().Submit(Valid("1", "2", "3", "4", "5", "6", "7"), "A. Sharma", Today);

        Assert.Equal("max", Assert.Single(result.Error!.Fields).Code);
    }
}
