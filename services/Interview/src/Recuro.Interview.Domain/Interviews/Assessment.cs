using Recuro.BuildingBlocks.Domain;

namespace Recuro.Interview.Domain.Interviews;

/// <summary>
/// One panel member's Annexure-B assessment of a round (RCU-INT-002/003). Drafts autosave; a submit
/// locks it. A later change is a superseding revision with a reason; earlier revisions are kept.
/// </summary>
public sealed class Assessment : Entity, ITenantOwned
{
    private List<Rating> _ratings = [];
    private List<string> _flags = [];
    private List<AssessmentRevision> _history = [];

    private Assessment()
    {
    }

    internal Assessment(Guid interviewId, PanelMember interviewer)
        : base(Guid.CreateVersion7())
    {
        InterviewId = interviewId;
        InterviewerId = interviewer.Id;
        InterviewerName = interviewer.Name;
        Status = AssessmentStatus.Pending;
    }

    public Guid TenantId { get; private set; }

    public Guid InterviewId { get; private set; }

    /// <summary>The interviewer's subject id (Keycloak <c>sub</c>). Only they, or HR-TA for them, may fill the form.</summary>
    public string InterviewerId { get; private set; } = string.Empty;

    public string InterviewerName { get; private set; } = string.Empty;

    public AssessmentStatus Status { get; private set; }

    public IReadOnlyList<Rating> Ratings => _ratings;

    public Recommendation? Recommendation { get; private set; }

    public string Justification { get; private set; } = string.Empty;

    /// <summary>Open points for BGV or the selection summary, e.g. "verify campus claims" (RCU-INT-005).</summary>
    public IReadOnlyList<string> Flags => _flags;

    /// <summary>Average of the current revision; N/A rows excluded.</summary>
    public decimal? Average { get; private set; }

    /// <summary>0 until submitted, then 1, 2… with each superseding revision.</summary>
    public int Revision { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public string? SubmittedBy { get; private set; }

    /// <summary>Earlier submitted revisions, oldest first.</summary>
    public IReadOnlyList<AssessmentRevision> History => _history;

    /// <summary>Optimistic concurrency token (PostgreSQL xmin), exposed as the ETag for autosave.</summary>
    public uint Version { get; private set; }

    public bool IsSubmitted => Status == AssessmentStatus.Submitted;

    internal Result SaveDraft(IReadOnlyList<string> competencies, AssessmentInput input)
    {
        var move = InterviewTransitions.Assessments.EnsureCanMove(Status, AssessmentStatus.Draft, $"Assessment {Id}");
        if (move.IsFailure)
        {
            return InterviewErrors.AssessmentLocked(Id);
        }

        var shape = Scoring.CheckShape(competencies, input.Ratings);
        if (shape.Count > 0)
        {
            return InterviewErrors.Invalid(shape);
        }

        Apply(input);
        Status = AssessmentStatus.Draft;
        return Result.Success();
    }

    internal Result Submit(IReadOnlyList<string> competencies, AssessmentInput input, string by, DateTimeOffset now)
    {
        if (IsSubmitted)
        {
            return InterviewErrors.AssessmentLocked(Id);
        }

        var checkedInput = Validate(competencies, input);
        if (checkedInput.IsFailure)
        {
            return checkedInput;
        }

        Apply(input);
        Status = AssessmentStatus.Submitted;
        Revision = 1;
        SubmittedAt = now;
        SubmittedBy = by;
        return Result.Success();
    }

    /// <summary>RCU-INT-003: a submitted record changes only by a superseding revision with a reason.</summary>
    internal Result Supersede(IReadOnlyList<string> competencies, AssessmentInput input, string reason, string by, DateTimeOffset now)
    {
        if (!IsSubmitted)
        {
            return InterviewErrors.NotSubmitted(Id);
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < InterviewLimits.MinReasonLength)
        {
            return InterviewErrors.ReasonRequired();
        }

        var checkedInput = Validate(competencies, input);
        if (checkedInput.IsFailure)
        {
            return checkedInput;
        }

        // The kept revision records why it was replaced.
        _history.Add(new AssessmentRevision(
            Revision, _ratings, Recommendation!.Value, Justification, _flags, Average, SubmittedAt!.Value, SubmittedBy ?? string.Empty, reason.Trim()));
        Apply(input);
        Revision++;
        SubmittedAt = now;
        SubmittedBy = by;
        return Result.Success();
    }

    private static Result Validate(IReadOnlyList<string> competencies, AssessmentInput input)
    {
        var problems = Scoring.CheckShape(competencies, input.Ratings).Concat(Scoring.CheckComplete(competencies, input.Ratings)).ToList();
        if (input.Recommendation is null)
        {
            problems.Add(("recommendation", "required", "Select an overall recommendation."));
        }

        if (string.IsNullOrWhiteSpace(input.Justification))
        {
            problems.Add(("justification", "required", "Justification is required."));
        }

        return problems.Count == 0 ? Result.Success() : InterviewErrors.Invalid(problems);
    }

    private void Apply(AssessmentInput input)
    {
        _ratings = input.Ratings.Select(r => r with { Comment = r.Comment.Trim(), Score = r.Na ? null : r.Score }).ToList();
        Recommendation = input.Recommendation;
        Justification = input.Justification.Trim();
        _flags = input.Flags.Select(f => f.Trim()).Where(f => f.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        Average = Scoring.Average(_ratings);
    }
}

/// <summary>What an interviewer (or HR-TA for them) sends: the form's rows, recommendation, justification and flags.</summary>
public sealed record AssessmentInput(
    IReadOnlyList<Rating> Ratings,
    Recommendation? Recommendation,
    string Justification,
    IReadOnlyList<string> Flags);

/// <summary>A panel member: subject id and display name.</summary>
public sealed record PanelMember(string Id, string Name);
