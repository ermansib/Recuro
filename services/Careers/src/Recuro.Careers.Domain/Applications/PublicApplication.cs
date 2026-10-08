using Recuro.BuildingBlocks.Domain;

namespace Recuro.Careers.Domain.Applications;

/// <summary>Steps of the candidate intake saga (RCU-BKD-001 §6.2) for one public application.</summary>
public enum IntakeState
{
    /// <summary>Consents captured; nothing created in other services yet.</summary>
    Started,

    /// <summary>The Candidate service holds the person (new or an existing record).</summary>
    CandidateRecorded,

    /// <summary>The Pipeline application exists; the applicant has an APP-ID.</summary>
    Completed,

    /// <summary>A step failed and nothing needed undoing.</summary>
    Failed,

    /// <summary>The application step failed and the candidate this saga created was tombstoned.</summary>
    Compensated,

    /// <summary>The application step failed and the tombstone call failed too; retention purges the record later.</summary>
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
}

/// <summary>A consent the applicant gave on the form, with the exact wording version (RCU-CAR-002).</summary>
public sealed record GivenConsent(string Type, string TextVersion, DateTimeOffset At);

/// <summary>
/// One application made on the public careers site: the consents, the intake saga's progress and,
/// once it has an APP-ID, the coarse status the applicant can look up (RCU-CAR-002/004/005/006).
/// Holds no contact details: those live only in the Candidate service.
/// </summary>
public sealed class PublicApplication : AggregateRoot, ITenantOwned
{
    private readonly List<GivenConsent> _consents = [];

    private PublicApplication()
    {
    }

    public Guid TenantId { get; private set; }

    public string PostingId { get; private set; } = string.Empty;

    public string ReqId { get; private set; } = string.Empty;

    public string Position { get; private set; } = string.Empty;

    /// <summary>Hash of the client's Idempotency-Key and email, so a double submit returns the first result.</summary>
    public string? ClientKey { get; private set; }

    public IReadOnlyList<GivenConsent> Consents => _consents;

    public IntakeState State { get; private set; }

    public string? CandidateId { get; private set; }

    /// <summary>True when this saga created the candidate, so a failed application step must tombstone it.</summary>
    public bool CandidateCreatedHere { get; private set; }

    /// <summary>The existing candidate the applicant matched (RCU-CND-002). For HR only; never returned publicly.</summary>
    public string? DuplicateOf { get; private set; }

    public string? AppId { get; private set; }

    public string? FailureCode { get; private set; }

    public PublicStage PublicStage { get; private set; }

    /// <summary>The last Pipeline stage reported for this application.</summary>
    public string? PipelineStage { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>RCU-CAR-006: the final-rejection event, used to match the regret email's delivery receipt.</summary>
    public Guid? FinalRejectedEventId { get; private set; }

    public DateOnly? RegretSendAt { get; private set; }

    public DateTimeOffset? RegretDeliveredAt { get; private set; }

    public static PublicApplication Start(
        string postingId,
        string reqId,
        string position,
        string? clientKey,
        IReadOnlyList<GivenConsent> consents,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(consents);
        var application = new PublicApplication
        {
            Id = Guid.CreateVersion7(),
            PostingId = postingId,
            ReqId = reqId,
            Position = position,
            ClientKey = clientKey,
            State = IntakeState.Started,
            PublicStage = PublicStage.Received,
            SubmittedAt = now,
            UpdatedAt = now,
        };
        application._consents.AddRange(consents);
        return application;
    }

    public Result CandidateRecorded(string candidateId, bool createdHere, DateTimeOffset now)
    {
        var moved = Move(IntakeState.CandidateRecorded, now);
        if (moved.IsSuccess)
        {
            (CandidateId, CandidateCreatedHere, DuplicateOf) = (candidateId, createdHere, createdHere ? null : candidateId);
        }

        return moved;
    }

    /// <summary>The application exists in Pipeline: the saga is done and <c>career.job.applied</c> goes out.</summary>
    public Result Complete(string appId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        var moved = Move(IntakeState.Completed, now);
        if (moved.IsFailure)
        {
            return moved;
        }

        (AppId, PipelineStage) = (appId, "Sourced");
        Raise(new PublicApplicationCompleted(this));
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

    /// <summary>Pipeline moved the application. Only the coarse stage is kept (RCU-CAR-005).</summary>
    public void StageChanged(string pipelineStage, DateTimeOffset now)
    {
        (PipelineStage, PublicStage, UpdatedAt) = (pipelineStage, PublicStages.FromPipeline(pipelineStage), now);
    }

    /// <summary>RCU-CAR-006: Pipeline rejected it for good; Notification sends the regret email on <paramref name="regretSendAt"/>.</summary>
    public void FinallyRejected(Guid eventId, DateOnly regretSendAt, DateTimeOffset now)
    {
        StageChanged("Rejected", now);
        (FinalRejectedEventId, RegretSendAt) = (eventId, regretSendAt);
    }

    /// <summary>Notification reported the regret email as sent; logged on the application.</summary>
    public void RegretDelivered(DateTimeOffset at) => RegretDeliveredAt ??= at;

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

/// <summary>The saga finished: publish <c>career.job.applied.v1</c> in the same transaction.</summary>
public sealed record PublicApplicationCompleted(PublicApplication Application) : IDomainEvent;
