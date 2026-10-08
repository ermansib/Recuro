using System.Globalization;
using System.Text.Json.Serialization;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Interview.Application.Abstractions;
using Recuro.Interview.Application.Interviews;
using Recuro.Interview.Domain.Interviews;
using Recuro.Interview.Domain.Selection;

namespace Recuro.Interview.Application.Selection;

public sealed record PanelVerdictDto(string Interviewer, string Status, decimal? Average, Recommendation? Recommendation, string? SubmittedAt);

public sealed record RoundSummaryDto(string InterviewId, string Round, string RoundType, InterviewStatus Status, decimal? Average, IReadOnlyList<PanelVerdictDto> Panel);

public sealed record SelectionStateDto(SelectionStatus Status, bool RatificationRequired, string SubmittedBy, string SubmittedAt, string? DecidedBy, string? DecidedAt, string? Reason);

/// <summary>
/// RCU-INT-005: per-round averages, the overall average, recommendations and open flags for one
/// application. <c>complete</c> is true once every round has all its feedback.
/// </summary>
public sealed record SelectionSummaryDto(
    string AppId,
    string ReqId,
    string Grade,
    IReadOnlyList<RoundSummaryDto> Rounds,
    decimal? OverallAverage,
    Recommendation? Recommendation,
    IReadOnlyList<string> OpenFlags,
    bool Complete,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SelectionStateDto? Selection)
{
    /// <summary>Lines for the PDF snapshot.</summary>
    public IReadOnlyList<string> ToLines()
    {
        var lines = new List<string>
        {
            $"Application: {AppId}   Requisition: {ReqId}   Grade: {Grade}",
            $"Overall average: {Format(OverallAverage)} / 5   Recommendation: {Recommendation?.ToString() ?? "-"}   Complete: {(Complete ? "yes" : "no")}",
            string.Empty,
        };
        foreach (var round in Rounds)
        {
            lines.Add($"{round.Round} ({round.Status}) - average {Format(round.Average)}");
            lines.AddRange(round.Panel.Select(p => $"    {p.Interviewer}: {p.Status}, {Format(p.Average)}, {p.Recommendation?.ToString() ?? "-"}"));
        }

        lines.Add(string.Empty);
        lines.Add(OpenFlags.Count == 0 ? "Open flags: none" : "Open flags:");
        lines.AddRange(OpenFlags.Select(f => $"    - {f}"));
        if (Selection is { } s)
        {
            lines.Add(string.Empty);
            lines.Add($"Selection: {s.Status} (submitted by {s.SubmittedBy} on {s.SubmittedAt}){(s.DecidedBy is null ? string.Empty : $", decided by {s.DecidedBy}")}");
        }

        return lines;
    }

    private static string Format(decimal? value) => value?.ToString("0.0", CultureInfo.InvariantCulture) ?? "-";
}

internal static class SelectionSummaryBuilder
{
    public static SelectionSummaryDto Build(string appId, IReadOnlyList<InterviewRound> all, SelectionDecision? decision)
    {
        var rounds = all.Where(r => r.Status != InterviewStatus.Cancelled).OrderBy(r => r.RoundNumber).ToList();
        var submitted = rounds.SelectMany(r => r.Assessments).Where(a => a.IsSubmitted).ToList();
        var summaries = rounds
            .Select(r => new RoundSummaryDto(
                r.Id.ToString(),
                r.Title,
                r.RoundType,
                r.Status,
                Scoring.MeanOf(r.Assessments.Where(a => a.IsSubmitted).Select(a => a.Average)),
                r.Assessments
                    .Select(a => new PanelVerdictDto(
                        a.InterviewerName,
                        a.Status == AssessmentStatus.Pending ? "Scheduled" : a.Status.ToString(),
                        a.Average,
                        a.Recommendation,
                        a.SubmittedAt is { } at ? InterviewRoundDto.Iso(at) : null))
                    .ToList()))
            .ToList();

        return new SelectionSummaryDto(
            appId,
            rounds.LastOrDefault()?.ReqId ?? decision?.ReqId ?? string.Empty,
            rounds.LastOrDefault()?.Grade ?? decision?.Grade ?? string.Empty,
            summaries,
            Scoring.MeanOf(summaries.Select(s => s.Average)),
            Consensus(submitted.Select(a => a.Recommendation).OfType<Recommendation>().ToList()),
            submitted.SelectMany(a => a.Flags).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            IsComplete(rounds),
            decision is null ? null : new SelectionStateDto(
                decision.Status,
                decision.RatificationRequired,
                decision.SubmittedBy,
                InterviewRoundDto.Iso(decision.SubmittedAt),
                decision.DecidedBy,
                decision.DecidedAt is { } d ? InterviewRoundDto.Iso(d) : null,
                decision.Reason));
    }

    public static bool IsComplete(IReadOnlyList<InterviewRound> rounds) =>
        rounds.Count > 0 && rounds.All(r => r.Status == InterviewStatus.Completed);

    /// <summary>The most common recommendation; a tie goes to the more cautious one.</summary>
    private static Recommendation? Consensus(List<Recommendation> recommendations) =>
        recommendations.Count == 0
            ? null
            : recommendations.GroupBy(r => r).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
}

public sealed record GetSelectionSummaryQuery(string AppId) : IQuery<SelectionSummaryDto>;

internal sealed class GetSelectionSummaryQueryHandler(IInterviewRepository interviews, ISelectionRepository selections)
    : IQueryHandler<GetSelectionSummaryQuery, SelectionSummaryDto>
{
    public async Task<Result<SelectionSummaryDto>> Handle(GetSelectionSummaryQuery query, CancellationToken ct)
    {
        var rounds = await interviews.ListForApplicationAsync(query.AppId, ct);
        var decision = await selections.GetByAppIdAsync(query.AppId, ct);
        if (rounds.Count == 0 && decision is null)
        {
            return InterviewErrors.NoInterview(query.AppId);
        }

        return SelectionSummaryBuilder.Build(query.AppId, rounds, decision);
    }
}

/// <summary>Renders a document for download. The Infrastructure layer writes the PDF.</summary>
public interface IPdfRenderer
{
    byte[] Render(string title, IReadOnlyList<string> lines);
}

/// <summary>RCU-INT-005: the summary as a PDF snapshot.</summary>
public sealed record GetSelectionSummaryPdfQuery(string AppId) : IQuery<byte[]>;

internal sealed class GetSelectionSummaryPdfQueryHandler(
    IQueryHandler<GetSelectionSummaryQuery, SelectionSummaryDto> summaries,
    IPdfRenderer pdf,
    TimeProvider clock) : IQueryHandler<GetSelectionSummaryPdfQuery, byte[]>
{
    public async Task<Result<byte[]>> Handle(GetSelectionSummaryPdfQuery query, CancellationToken ct)
    {
        var summary = await summaries.Handle(new GetSelectionSummaryQuery(query.AppId), ct);
        if (summary.IsFailure)
        {
            return summary.Error!;
        }

        var lines = summary.Value.ToLines().Append(string.Empty).Append($"Snapshot taken {InterviewRoundDto.Iso(clock.GetUtcNow())}").ToList();
        return pdf.Render($"Selection summary - {query.AppId}", lines);
    }
}
