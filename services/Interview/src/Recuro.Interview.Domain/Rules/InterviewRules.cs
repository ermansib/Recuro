using Recuro.Interview.Domain.Interviews;

namespace Recuro.Interview.Domain.Rules;

/// <summary>A round a grade's template allows, e.g. <c>functional</c> / "Functional Interview".</summary>
public sealed record RoundTemplate(string Type, string Label);

/// <summary>The grades whose selection needs ratification and who ratifies (RCU-INT-006).</summary>
public sealed record RatificationRule(IReadOnlyList<string> Grades, string Role, string Label, int SlaWorkingDays);

/// <summary>
/// The interview rules this service applies: round templates per grade (RCU-INT-001), the feedback SLA
/// (RCU-INT-004) and the ratification rule (RCU-INT-006). They come from the Config service's
/// <c>interview</c> matrix; <see cref="FrdDefault"/> is used until a tenant's Config has one.
/// </summary>
public sealed record InterviewRules(
    string ConfigVersionId,
    IReadOnlyDictionary<string, IReadOnlyList<RoundTemplate>> Templates,
    TimeSpan ReminderAfter,
    TimeSpan OverdueAfter,
    RatificationRule Ratification)
{
    public const string DefaultVersion = "frd-default";

    private static readonly RoundTemplate HrScreening = new("hr-screening", "HR Screening");
    private static readonly RoundTemplate Functional = new("functional", "Functional Interview");
    private static readonly RoundTemplate Hod = new("hod", "HOD Interview");
    private static readonly RoundTemplate CaseStudy = new("case-study", "Case Study Presentation");
    private static readonly RoundTemplate Leadership = new("leadership", "Leadership Interview");
    private static readonly RoundTemplate Board = new("board-panel", "Board / NRC Panel");

    /// <summary>
    /// FRD defaults (§9.6 and the 48h feedback SLA of §12/§16). Seed values, not policy in code: a
    /// tenant's Config <c>interview</c> matrix replaces them.
    /// </summary>
    public static InterviewRules FrdDefault { get; } = new(
        DefaultVersion,
        new Dictionary<string, IReadOnlyList<RoundTemplate>>(StringComparer.Ordinal)
        {
            ["E"] = [HrScreening, Functional],
            ["M1"] = [HrScreening, Functional, Hod],
            ["M3"] = [HrScreening, Functional, Hod, CaseStudy],
            ["VP"] = [HrScreening, Functional, Leadership, CaseStudy],
            ["KMP"] = [HrScreening, Leadership, Board],
        },
        TimeSpan.FromHours(24),
        TimeSpan.FromHours(48),
        new RatificationRule(["M3", "VP", "KMP"], "hrhead", "HR Head", 2));

    public FeedbackSla Sla => new(ReminderAfter, OverdueAfter, ConfigVersionId);

    public RoundTemplate? FindRound(string grade, string roundType) =>
        Templates.TryGetValue(grade, out var rounds) ? rounds.FirstOrDefault(r => r.Type == roundType) : null;

    public bool HasTemplate(string grade) => Templates.ContainsKey(grade);

    public bool NeedsRatification(string grade) => Ratification.Grades.Contains(grade, StringComparer.Ordinal);
}
