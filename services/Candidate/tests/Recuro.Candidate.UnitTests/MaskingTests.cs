using Recuro.Candidate.Application.Candidates;
using Recuro.Candidate.Application.Candidates.Masking;

namespace Recuro.Candidate.UnitTests;

public class MaskingTests
{
    private static readonly CandidateDto Sample = new(
        "id", "Rahul Mehta", "rahul.mehta@email.example", "+91 98200 40001", 8, "8 yrs", 19, 21, 30, "Portal", null,
        [new ConsentDto("DataPrivacy", "v1", "2026-08-01T10:00:00Z")]);

    private static readonly Dictionary<string, string> MdCeoWire = new(StringComparer.Ordinal)
    {
        ["currentCtc"] = "hide",
        ["expectedCtc"] = "hide",
        ["phone"] = "partial",
        ["email"] = "partial",
    };

    private static CandidateDto Mask(params IReadOnlyDictionary<string, MaskStrategy>?[] roleMaps) =>
        CandidateMasking.Apply(Sample, CandidateMasking.Combine(roleMaps), (field, value) => $"h:{field}");

    [Fact]
    public void An_empty_map_shows_everything()
    {
        Assert.Equal(Sample, Mask(CandidateMasking.Parse(new Dictionary<string, string>())));
    }

    [Fact]
    public void MD_CEO_sees_CTC_hidden_and_contact_details_with_the_last_four_characters()
    {
        var masked = Mask(CandidateMasking.Parse(MdCeoWire));

        Assert.Null(masked.CurrentCtc);
        Assert.Null(masked.ExpectedCtc);
        Assert.Equal("••••mple", masked.Email);
        Assert.Equal("••••0001", masked.Phone);
        Assert.Equal("Rahul Mehta", masked.Name);
    }

    [Fact]
    public void Hash_replaces_text_and_hides_amounts()
    {
        var masked = Mask(CandidateMasking.Parse(new Dictionary<string, string> { ["phone"] = "hash", ["currentCtc"] = "hash" }));

        Assert.Equal("h:phone", masked.Phone);
        Assert.Null(masked.CurrentCtc);
        Assert.Equal(21, masked.ExpectedCtc);
    }

    [Fact]
    public void A_missing_map_or_unknown_strategy_fails_closed()
    {
        var masked = Mask([null]);
        Assert.Equal(string.Empty, masked.Name);
        Assert.Equal(string.Empty, masked.Email);
        Assert.Null(masked.CurrentCtc);

        Assert.Equal(string.Empty, Mask(CandidateMasking.Parse(new Dictionary<string, string> { ["email"] = "scramble" })).Email);
    }

    [Fact]
    public void The_least_restrictive_role_wins()
    {
        Assert.Equal(Sample, Mask(CandidateMasking.Parse(MdCeoWire), CandidateMasking.Parse(new Dictionary<string, string>())));
        Assert.Equal("••••0001", Mask(CandidateMasking.Parse(MdCeoWire), null).Phone);
    }

    [Theory]
    [InlineData("hrta")]
    [InlineData("hrhead")]
    [InlineData("mdceo")]
    public void The_local_fallback_matches_the_FRD_table(string role)
    {
        var masked = Mask(CandidateMasking.LocalFallback(role));

        Assert.Equal(role == "mdceo" ? null : Sample.CurrentCtc, masked.CurrentCtc);
        Assert.Equal("Rahul Mehta", masked.Name);
    }

    [Fact]
    public void The_local_fallback_fails_closed_for_other_roles()
    {
        Assert.Null(CandidateMasking.LocalFallback("service"));
        Assert.Null(CandidateMasking.LocalFallback("employee"));
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
