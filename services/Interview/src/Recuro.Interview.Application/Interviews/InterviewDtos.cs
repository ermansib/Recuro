using System.Globalization;
using System.Text.Json.Serialization;
using Recuro.Interview.Domain.Interviews;

namespace Recuro.Interview.Application.Interviews;

/// <summary>One row of the frontend's <c>InterviewRound.ratings</c>.</summary>
public sealed record RatingDto(string CompetencyId, int? Score, bool Na, string Comment);

/// <summary>One line of the frontend's <c>InterviewRound.history</c>; <c>tone</c> is a <c>ChipTone</c>.</summary>
public sealed record RoundHistoryDto(string Round, string Status, string Tone);

/// <summary>
/// The frontend's <c>InterviewRound</c> (frontend/src/domain/types.ts), field for field: one panel
/// member's Annexure-B form for one round. <c>id</c> is the assessment id.
/// </summary>
public sealed record InterviewRoundDto(
    string Id,
    string AppId,
    string Round,
    string Interviewer,
    InterviewMode Mode,
    string ScheduledFor,
    string FeedbackDueAt,
    string Status,
    IReadOnlyList<RatingDto> Ratings,
    Recommendation? Recommendation,
    string Justification,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SubmittedAt,
    IReadOnlyList<RoundHistoryDto> History)
{
    /// <summary>Maps one assessment. <paramref name="rounds"/> are the application's rounds, for the history strip.</summary>
    public static InterviewRoundDto From(InterviewRound round, Assessment assessment, IReadOnlyList<InterviewRound> rounds)
    {
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(rounds);
        var ratings = round.Competencies
            .Select(c => assessment.Ratings.FirstOrDefault(r => r.CompetencyId == c) is { } r
                ? new RatingDto(c, r.Score, r.Na, r.Comment)
                : new RatingDto(c, null, false, string.Empty))
            .ToList();

        return new InterviewRoundDto(
            assessment.Id.ToString(),
            round.AppId,
            round.Title,
            assessment.InterviewerName,
            round.Mode,
            Iso(round.ScheduledFor),
            Iso(round.OverdueAt),
            assessment.Status == AssessmentStatus.Pending ? "Scheduled" : assessment.Status.ToString(),
            ratings,
            assessment.Recommendation,
            assessment.Justification,
            assessment.SubmittedAt is { } at ? Iso(at) : null,
            rounds.Where(r => r.Status != InterviewStatus.Cancelled).OrderBy(r => r.RoundNumber).Select(r => HistoryLine(r, r.Id == round.Id)).ToList());
    }

    public static string Iso(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);

    private static RoundHistoryDto HistoryLine(InterviewRound round, bool current)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"R{round.RoundNumber} — {round.RoundLabel}{(current ? " (this)" : string.Empty)}");
        var average = Scoring.MeanOf(round.Assessments.Where(a => a.IsSubmitted).Select(a => a.Average));
        return round.Status == InterviewStatus.Completed
            ? new RoundHistoryDto(name, average is { } avg ? string.Create(CultureInfo.InvariantCulture, $"✓ {avg:0.0}") : "✓", "green")
            : new RoundHistoryDto(name, "● pending", "amber");
    }
}

/// <summary>A panel seat on a round: who, and where their feedback stands.</summary>
public sealed record PanelSeatDto(
    string AssessmentId,
    string InterviewerId,
    string Interviewer,
    string Status,
    decimal? Average,
    Recommendation? Recommendation,
    int Revision);

/// <summary>A round as HR-TA manages it (schedule, reschedule, list for an application).</summary>
public sealed record InterviewDto(
    string Id,
    string AppId,
    string ReqId,
    string Grade,
    string RoundType,
    string Round,
    int RoundNumber,
    InterviewMode Mode,
    string ScheduledFor,
    string EndsAt,
    string FeedbackDueAt,
    InterviewStatus Status,
    IReadOnlyList<string> Competencies,
    IReadOnlyList<string> Attachments,
    IReadOnlyList<PanelSeatDto> Panel)
{
    public static InterviewDto From(InterviewRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        return new InterviewDto(
            round.Id.ToString(),
            round.AppId,
            round.ReqId,
            round.Grade,
            round.RoundType,
            round.Title,
            round.RoundNumber,
            round.Mode,
            InterviewRoundDto.Iso(round.ScheduledFor),
            InterviewRoundDto.Iso(round.EndsAt),
            InterviewRoundDto.Iso(round.OverdueAt),
            round.Status,
            round.Competencies,
            round.Attachments,
            round.Assessments
                .Select(a => new PanelSeatDto(
                    a.Id.ToString(),
                    a.InterviewerId,
                    a.InterviewerName,
                    a.Status == AssessmentStatus.Pending ? "Scheduled" : a.Status.ToString(),
                    a.Average,
                    a.Recommendation,
                    a.Revision))
                .ToList());
    }
}

/// <summary>An assessment with its concurrency version, which the API returns as the ETag.</summary>
public sealed record VersionedRound(InterviewRoundDto Round, uint Version);

/// <summary>What the client sends to autosave, submit or supersede a form (the frontend's <c>InterviewRound</c> fields it edits).</summary>
public sealed record AssessmentRequest(
    IReadOnlyList<RatingDto>? Ratings,
    Recommendation? Recommendation,
    string? Justification,
    IReadOnlyList<string>? Flags,
    string? Reason)
{
    public AssessmentInput ToInput() => new(
        (Ratings ?? []).Select(r => new Rating(r.CompetencyId?.Trim() ?? string.Empty, r.Score, r.Na, r.Comment ?? string.Empty)).ToList(),
        Recommendation,
        Justification ?? string.Empty,
        Flags ?? []);
}
