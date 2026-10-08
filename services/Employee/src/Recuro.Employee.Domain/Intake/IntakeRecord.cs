using Recuro.BuildingBlocks.Domain;

namespace Recuro.Employee.Domain.Intake;

/// <summary>Steps of the internal candidate intake saga (RCU-BKD-001 §6.2: owner Employee for IJP and referrals).</summary>
public enum IntakeState
{
    /// <summary>Recorded here; nothing created in other services yet.</summary>
    Started,

    /// <summary>The Candidate service holds the person (new or an existing record).</summary>
    CandidateRecorded,

    /// <summary>The Pipeline application exists.</summary>
    Completed,

    /// <summary>A step failed and nothing needed undoing.</summary>
    Failed,

    /// <summary>The application step failed and the candidate this saga created was tombstoned.</summary>
    Compensated,

    /// <summary>The application step failed and so did the tombstone; retention purges the record later.</summary>
    CompensationFailed,
}

/// <summary>The saga's legal moves as data.</summary>
public static class IntakeTransitions
{
    public static readonly TransitionTable<IntakeState> Table = new(new Dictionary<IntakeState, IntakeState[]>
    {
        [IntakeState.Started] = [IntakeState.CandidateRecorded, IntakeState.Failed],
        [IntakeState.CandidateRecorded] = [IntakeState.Completed, IntakeState.Failed, IntakeState.Compensated, IntakeState.CompensationFailed],
        [IntakeState.Completed] = [],
        [IntakeState.Failed] = [],
        [IntakeState.Compensated] = [],
        [IntakeState.CompensationFailed] = [],
    });

    /// <summary>States in which the record still counts as an application in progress.</summary>
    public static bool IsLive(IntakeState state) => state is IntakeState.Started or IntakeState.CandidateRecorded or IntakeState.Completed;
}

/// <summary>RCU-EMP-005: what an employee may see of an application's progress. No interviewer notes, no HR detail.</summary>
public enum ProgressStage
{
    Received,
    Screening,
    Interview,
    Offer,
    Joined,
    Closed,
}

/// <summary>Maps Pipeline stages (FRD §6.2) to <see cref="ProgressStage"/>, as data.</summary>
public static class ProgressStages
{
    private static readonly Dictionary<string, ProgressStage> ByPipelineStage = new(StringComparer.Ordinal)
    {
        ["Sourced"] = ProgressStage.Received,
        ["Screened"] = ProgressStage.Screening,
        ["Hold"] = ProgressStage.Screening,
        ["Interview"] = ProgressStage.Interview,
        ["Selection"] = ProgressStage.Interview,
        ["BGV"] = ProgressStage.Offer,
        ["Offer"] = ProgressStage.Offer,
        ["PreBoarding"] = ProgressStage.Offer,
        ["Onboarded"] = ProgressStage.Joined,
        ["Confirmed"] = ProgressStage.Joined,
        ["Rejected"] = ProgressStage.Closed,
        ["Withdrawn"] = ProgressStage.Closed,
    };

    /// <summary>An unknown stage (a newer Pipeline) reads as screening rather than failing.</summary>
    public static ProgressStage FromPipeline(string stage) =>
        ByPipelineStage.TryGetValue(stage, out var mapped) ? mapped : ProgressStage.Screening;
}

/// <summary>
/// What an IJP application and a referral share: who raised it, for which requisition, and the intake
/// saga's progress up to the Pipeline application whose stage is then mirrored coarsely.
/// </summary>
public abstract class IntakeRecord : AggregateRoot, ITenantOwned
{
    public Guid TenantId { get; private set; }

    /// <summary>The employee who applied or referred (token subject).</summary>
    public string EmployeeId { get; protected set; } = string.Empty;

    public string EmployeeName { get; protected set; } = string.Empty;

    public string ReqId { get; protected set; } = string.Empty;

    public IntakeState State { get; private set; }

    public string? CandidateId { get; private set; }

    /// <summary>True when this saga created the candidate, so a failed application step must tombstone it.</summary>
    public bool CandidateCreatedHere { get; private set; }

    public string? AppId { get; private set; }

    public string? FailureCode { get; private set; }

    public string? PipelineStage { get; private set; }

    public ProgressStage Progress { get; private set; }

    public DateTimeOffset SubmittedAt { get; protected set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Still an application in progress (for the one-active-application rule).</summary>
    public bool IsActive => IntakeTransitions.IsLive(State) && Progress is not (ProgressStage.Closed or ProgressStage.Joined);

    public Result CandidateRecorded(string candidateId, bool createdHere, DateTimeOffset now)
    {
        var moved = Move(IntakeState.CandidateRecorded, now);
        if (moved.IsSuccess)
        {
            (CandidateId, CandidateCreatedHere) = (candidateId, createdHere);
        }

        return moved;
    }

    public Result Complete(string appId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        var moved = Move(IntakeState.Completed, now);
        if (moved.IsFailure)
        {
            return moved;
        }

        (AppId, PipelineStage, Progress) = (appId, "Sourced", ProgressStage.Received);
        OnCompleted();
        return Result.Success();
    }

    /// <summary>A step failed. <paramref name="compensated"/> says whether the candidate this saga created was tombstoned.</summary>
    public void Fail(string code, bool? compensated, DateTimeOffset now)
    {
        var to = compensated switch
        {
            true => IntakeState.Compensated,
            false => IntakeState.CompensationFailed,
            null => IntakeState.Failed,
        };
        if (Move(to, now).IsSuccess)
        {
            FailureCode = code;
        }
    }

    /// <summary>Pipeline moved the application; only the coarse stage is shown to the employee.</summary>
    public void StageChanged(string pipelineStage, DateTimeOffset now) =>
        (PipelineStage, Progress, UpdatedAt) = (pipelineStage, ProgressStages.FromPipeline(pipelineStage), now);

    protected void Begin(DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        (State, Progress, SubmittedAt, UpdatedAt) = (IntakeState.Started, ProgressStage.Received, now, now);
    }

    /// <summary>Raises the record's integration event once the Pipeline application exists.</summary>
    protected abstract void OnCompleted();

    private Result Move(IntakeState to, DateTimeOffset now)
    {
        var moved = IntakeTransitions.Table.EnsureCanMove(State, to, "Intake");
        if (moved.IsSuccess)
        {
            (State, UpdatedAt) = (to, now);
        }

        return moved;
    }
}
