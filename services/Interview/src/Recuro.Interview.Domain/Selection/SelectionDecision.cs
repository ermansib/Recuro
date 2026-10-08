using Recuro.BuildingBlocks.Domain;
using Recuro.Interview.Domain.Interviews;

namespace Recuro.Interview.Domain.Selection;

public enum SelectionStatus
{
    /// <summary>Waiting for HR Head ratification in the shared workflow (RCU-INT-006).</summary>
    PendingRatification,

    /// <summary>Selection stands; <c>interview.selection.ratified</c> was published.</summary>
    Ratified,

    /// <summary>The ratifier sent it back. HR-TA may submit again after more rounds.</summary>
    Rejected,
}

/// <summary>
/// HR-TA's consolidated selection for one application (RCU-INT-005/006). Grades the rules mark for
/// ratification (M3 and above by default) go through the shared workflow; others are ratified on submit.
/// </summary>
public sealed class SelectionDecision : AggregateRoot, ITenantOwned
{
    private SelectionDecision()
    {
    }

    public Guid TenantId { get; private set; }

    public string AppId { get; private set; } = string.Empty;

    public string ReqId { get; private set; } = string.Empty;

    public string Grade { get; private set; } = string.Empty;

    public decimal? OverallAverage { get; private set; }

    public int Rounds { get; private set; }

    public SelectionStatus Status { get; private set; }

    public bool RatificationRequired { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public string ConfigVersionId { get; private set; } = string.Empty;

    public string SubmittedBy { get; private set; } = string.Empty;

    public DateTimeOffset SubmittedAt { get; private set; }

    public string? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? Reason { get; private set; }

    public uint Version { get; private set; }

    public static TransitionTable<SelectionStatus> Transitions { get; } = new(new Dictionary<SelectionStatus, SelectionStatus[]>
    {
        [SelectionStatus.PendingRatification] = [SelectionStatus.Ratified, SelectionStatus.Rejected],
        [SelectionStatus.Ratified] = [],
        [SelectionStatus.Rejected] = [SelectionStatus.PendingRatification, SelectionStatus.Ratified],
    });

    /// <summary>
    /// Starts a selection. Without ratification it is ratified at once; with it, it waits for
    /// <see cref="AwaitRatification"/> to record the workflow.
    /// </summary>
    public static SelectionDecision Start(SelectionInput input, string by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        var decision = new SelectionDecision { Id = Guid.CreateVersion7(), AppId = input.AppId, Status = SelectionStatus.Rejected };
        decision.Resubmit(input, by, now);
        return decision;
    }

    /// <summary>Submits again after a rejection, with the latest rounds.</summary>
    public Result Resubmit(SelectionInput input, string by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Status != SelectionStatus.Rejected)
        {
            return InterviewErrors.SelectionAlreadySubmitted(AppId);
        }

        ReqId = input.ReqId;
        Grade = input.Grade;
        OverallAverage = input.OverallAverage;
        Rounds = input.Rounds;
        RatificationRequired = input.RatificationRequired;
        ConfigVersionId = input.ConfigVersionId;
        SubmittedBy = by;
        SubmittedAt = now;
        WorkflowInstanceId = null;
        DecidedBy = null;
        DecidedAt = null;
        Reason = null;

        if (RatificationRequired)
        {
            Status = SelectionStatus.PendingRatification;
        }
        else
        {
            Ratify(by, now);
        }

        return Result.Success();
    }

    public void AwaitRatification(Guid workflowInstanceId)
    {
        if (Status != SelectionStatus.PendingRatification)
        {
            throw new InvalidOperationException($"Selection for {AppId} is {Status}, not waiting for ratification.");
        }

        WorkflowInstanceId = workflowInstanceId;
    }

    /// <summary>The ratification workflow finished. A repeated outcome is a no-op (events are redelivered).</summary>
    public Result ApplyRatification(bool approved, string? by, string? reason, DateTimeOffset now)
    {
        var target = approved ? SelectionStatus.Ratified : SelectionStatus.Rejected;
        if (Status == target)
        {
            return Result.Success();
        }

        var move = Transitions.EnsureCanMove(Status, target, $"Selection for {AppId}");
        if (move.IsFailure)
        {
            return move;
        }

        if (approved)
        {
            Ratify(by ?? "workflow", now);
        }
        else
        {
            Status = SelectionStatus.Rejected;
            DecidedBy = by;
            DecidedAt = now;
            Reason = reason;
        }

        return Result.Success();
    }

    private void Ratify(string by, DateTimeOffset now)
    {
        Status = SelectionStatus.Ratified;
        DecidedBy = by;
        DecidedAt = now;
        Raise(new SelectionRatified(Id, AppId, ReqId, by, OverallAverage, Rounds));
    }
}

/// <summary>What submit computed from the rounds and the rules.</summary>
public sealed record SelectionInput(
    string AppId,
    string ReqId,
    string Grade,
    decimal? OverallAverage,
    int Rounds,
    bool RatificationRequired,
    string ConfigVersionId);

public sealed record SelectionRatified(Guid SelectionId, string AppId, string ReqId, string RatifiedBy, decimal? Average, int Rounds) : IDomainEvent;
