using Recuro.Candidate.Application.Candidates;
using Recuro.Candidate.Application.Candidates.Masking;

namespace Recuro.Candidate.UnitTests;

public class MaskingTests
{
    private static readonly CandidateDto Sample = new(
        "id", "Rahul Mehta", "rahul.mehta@email.example", "+91 98200 40001", 8, "8 yrs", 19, 21, 30, "Portal", null,
        [new ConsentDto("DataPrivacy", "v1", "2026-08-01T10:00:00Z")]);

    [Theory]
    [InlineData("hrta")]
    [InlineData("hrhead")]
    [InlineData("service")]
    public void HR_and_services_see_everything(string role)
    {
        Assert.Equal(Sample, CandidateMasking.Apply(Sample, CandidateMasking.For([role])));
    }

    [Fact]
    public void MD_CEO_sees_CTC_hidden_and_contact_details_partly_hidden()
    {
        var masked = CandidateMasking.Apply(Sample, CandidateMasking.For(["mdceo"]));

        Assert.Null(masked.CurrentCtc);
        Assert.Null(masked.ExpectedCtc);
        Assert.Equal("r***@email.example", masked.Email);
        Assert.Equal("••••••0001", masked.Phone);
        Assert.Equal("Rahul Mehta", masked.Name);
    }

    [Fact]
    public void Unknown_roles_fail_closed()
    {
        var masked = CandidateMasking.Apply(Sample, CandidateMasking.For(["employee"]));

        Assert.Equal(string.Empty, masked.Email);
        Assert.Equal(string.Empty, masked.Phone);
        Assert.Null(masked.CurrentCtc);
    }

    [Fact]
    public void The_least_restrictive_role_wins()
    {
        Assert.Equal(Sample, CandidateMasking.Apply(Sample, CandidateMasking.For(["mdceo", "hrhead"])));
    }

    [Fact]
    public void Source_names_round_trip_with_the_frontend_spelling()
    {
        Assert.True(CandidateSourceNames.TryParse("Walk-in", out var source));
        Assert.Equal("Walk-in", CandidateSourceNames.ToName(source));
        Assert.True(CandidateSourceNames.TryParse("IJP", out source));
        Assert.Equal("IJP", CandidateSourceNames.ToName(source));
        Assert.False(CandidateSourceNames.TryParse("Unknown", out _));
    }
}
