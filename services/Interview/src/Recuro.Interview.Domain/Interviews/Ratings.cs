namespace Recuro.Interview.Domain.Interviews;

/// <summary>One competency row of an Annexure-B form: a score of 1–5, or N/A (RCU-INT-003).</summary>
public sealed record Rating(string CompetencyId, int? Score, bool Na, string Comment);

/// <summary>A submitted assessment kept when a later revision supersedes it (RCU-INT-003: the original is retained).</summary>
public sealed record AssessmentRevision(
    int Revision,
    IReadOnlyList<Rating> Ratings,
    Recommendation Recommendation,
    string Justification,
    IReadOnlyList<string> Flags,
    decimal? Average,
    DateTimeOffset SubmittedAt,
    string SubmittedBy,
    string? SupersedeReason);

/// <summary>Scoring rules for Annexure B.</summary>
public static class Scoring
{
    /// <summary>Mean of the scored rows, one decimal; N/A rows are excluded. Null when nothing is scored.</summary>
    public static decimal? Average(IEnumerable<Rating> ratings)
    {
        ArgumentNullException.ThrowIfNull(ratings);
        var scores = ratings.Where(r => !r.Na && r.Score is not null).Select(r => (decimal)r.Score!.Value).ToList();
        return scores.Count == 0 ? null : Math.Round(scores.Average(), 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>Mean of several averages (rounds or panel members), ignoring the ones with no score.</summary>
    public static decimal? MeanOf(IEnumerable<decimal?> averages)
    {
        ArgumentNullException.ThrowIfNull(averages);
        var values = averages.OfType<decimal>().ToList();
        return values.Count == 0 ? null : Math.Round(values.Average(), 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// What a submit must contain: every competency on the form rated 1–5 or marked N/A, nothing else,
    /// and at least one scored row. Returns (field, code, message) for each problem.
    /// </summary>
    public static IReadOnlyList<(string Field, string Code, string Message)> CheckComplete(
        IReadOnlyList<string> competencies,
        IReadOnlyList<Rating> ratings)
    {
        ArgumentNullException.ThrowIfNull(competencies);
        ArgumentNullException.ThrowIfNull(ratings);
        var problems = new List<(string, string, string)>();
        foreach (var competency in competencies)
        {
            var row = ratings.FirstOrDefault(r => r.CompetencyId == competency);
            if (row is null || (!row.Na && row.Score is null))
            {
                problems.Add(($"ratings.{competency}", "rating_required", $"Rate {competency} from 1 to 5 or mark it N/A."));
            }
        }

        if (problems.Count == 0 && Average(ratings) is null)
        {
            problems.Add(("ratings", "no_scores", "At least one competency must be scored; not every row can be N/A."));
        }

        return problems;
    }

    /// <summary>Shape rules that hold for drafts too: known competencies, one row each, scores in range.</summary>
    public static IReadOnlyList<(string Field, string Code, string Message)> CheckShape(
        IReadOnlyList<string> competencies,
        IReadOnlyList<Rating> ratings)
    {
        ArgumentNullException.ThrowIfNull(competencies);
        ArgumentNullException.ThrowIfNull(ratings);
        var problems = new List<(string, string, string)>();
        foreach (var group in ratings.GroupBy(r => r.CompetencyId, StringComparer.Ordinal))
        {
            if (!competencies.Contains(group.Key, StringComparer.Ordinal))
            {
                problems.Add(($"ratings.{group.Key}", "unknown_competency", $"{group.Key} is not on this form."));
            }
            else if (group.Count() > 1)
            {
                problems.Add(($"ratings.{group.Key}", "duplicate_competency", $"{group.Key} is rated more than once."));
            }
        }

        foreach (var row in ratings.Where(r => r.Score is { } s && (s < InterviewLimits.MinScore || s > InterviewLimits.MaxScore)))
        {
            problems.Add(($"ratings.{row.CompetencyId}", "score_range", $"Scores run from {InterviewLimits.MinScore} to {InterviewLimits.MaxScore}."));
        }

        foreach (var row in ratings.Where(r => r.Na && r.Score is not null))
        {
            problems.Add(($"ratings.{row.CompetencyId}", "na_with_score", "A row marked N/A has no score."));
        }

        return problems;
    }
}
