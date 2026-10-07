using Recuro.BuildingBlocks.Domain;

namespace Recuro.Pipeline.Domain.Applications;

/// <summary>One line of <see cref="Application.StageHistory"/>.</summary>
public sealed record StageMove(ApplicationStage? From, ApplicationStage To, string By, string? ById, DateTimeOffset At);

/// <summary>RCU-PPL-003: why and until when, recorded at final rejection.</summary>
public sealed record Rejection(string Reason, DateOnly RegretDueBy, DateOnly RetainUntil);

/// <summary>
/// A candidate's application to one requisition, moving through the stage machine (FRD §6.2). The
/// aggregate guards every move; TAT clocks are the time spent in the current stage, frozen while on Hold.
/// </summary>
public sealed class Application : AggregateRoot, ITenantOwned
{
    private readonly List<StageMove> _stageHistory = [];

    private Application()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Business id, <c>APP-YYYY-####</c>, unique per tenant.</summary>
    public string AppId { get; private set; } = string.Empty;

    public string ReqId { get; private set; } = string.Empty;

    public string CandidateId { get; private set; } = string.Empty;

    /// <summary>Candidate source at application time (frontend spelling), for funnel and source-mix reporting.</summary>
    public string Source { get; private set; } = string.Empty;

    public ApplicationStage Stage { get; private set; }

    public IReadOnlyList<StageMove> StageHistory => _stageHistory;

    public string Note { get; private set; } = string.Empty;

    public Rejection? Rejection { get; private set; }

    /// <summary>When the TAT clock for the current stage started (shifted forward by any time on Hold).</summary>
    public DateTimeOffset StageEnteredAt { get; private set; }

    /// <summary>While on Hold: the stage and clock start to resume from (RCU-PPL-004).</summary>
    public ApplicationStage? HeldFromStage { get; private set; }

    public DateTimeOffset? HeldFromEnteredAt { get; private set; }

    public DateTimeOffset? HeldAt { get; private set; }

    /// <summary>Set once the current stage's TAT breach was raised, so it is raised once per stage (RCU-PPL-005).</summary>
    public DateTimeOffset? TatBreachedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Application Create(string appId, string reqId, string candidateId, string source, string note, StageActor actor, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reqId);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentNullException.ThrowIfNull(actor);

        var application = new Application
        {
            Id = Guid.CreateVersion7(),
            AppId = appId,
            ReqId = reqId,
            CandidateId = candidateId,
            Source = source,
            Stage = ApplicationStage.Sourced,
            Note = note,
            StageEnteredAt = now,
            CreatedAt = now,
        };
        application._stageHistory.Add(new StageMove(null, ApplicationStage.Sourced, actor.Name, actor.Id, now));
        application.Raise(new ApplicationCreatedDomainEvent(application));
        return application;
    }

    public bool IsClosed => ApplicationTransitions.IsClosed(Stage);

    /// <summary>RCU-PPL-002: a move along the state machine. Rejection has its own method because it needs a reason.</summary>
    public Result MoveTo(ApplicationStage to, StageActor actor, DateTimeOffset now)
    {
        if (to == ApplicationStage.Rejected)
        {
            return ApplicationErrors.UseReject;
        }

        return Transition(to, actor, now);
    }

    /// <summary>
    /// RCU-PPL-003: final rejection with a mandatory reason, the regret deadline (worked out on the
    /// tenant's business calendar by the caller) and the retention date.
    /// </summary>
    public Result Reject(string reason, StageActor actor, DateTimeOffset now, DateOnly regretDueBy, int retentionDays)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return ApplicationErrors.ReasonRequired;
        }

        var moved = Transition(ApplicationStage.Rejected, actor, now);
        if (moved.IsFailure)
        {
            return moved;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        Rejection = new Rejection(reason.Trim(), regretDueBy, today.AddDays(retentionDays));
        Note = $"Rejected — {Rejection.Reason}";
        Raise(new FinalRejectedDomainEvent(this, Rejection.Reason, Rejection.RegretDueBy, Rejection.RetainUntil));
        return Result.Success();
    }

    /// <summary>
    /// Moves on when another service reports progress (selection ratified, BGV cleared, offer accepted),
    /// but only from the stage that progress belongs to, so a redelivered or late event changes nothing.
    /// </summary>
    public Result Advance(ApplicationStage expectedFrom, ApplicationStage to, StageActor actor, DateTimeOffset now) =>
        Stage == expectedFrom ? Transition(to, actor, now) : Result.Success();

    /// <summary>
    /// RCU-PPL-005: raises the TAT breach for the current stage once, when <paramref name="dueAt"/> has passed.
    /// Returns true when a breach was raised.
    /// </summary>
    public bool FlagTatBreach(DateTimeOffset dueAt, DateTimeOffset now, string? escalation = null)
    {
        if (TatBreachedAt is not null || now <= dueAt || Stage == ApplicationStage.Hold || IsClosed)
        {
            return false;
        }

        TatBreachedAt = now;
        Raise(new TatBreachedDomainEvent(this, Stage, dueAt, now - dueAt, escalation));
        return true;
    }

    private Result Transition(ApplicationStage to, StageActor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var allowed = ApplicationTransitions.Table.EnsureCanMove(Stage, to, "Application");
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var from = Stage;
        var dwell = now - StageEnteredAt;
        if (to == ApplicationStage.Hold)
        {
            (HeldFromStage, HeldFromEnteredAt, HeldAt) = (from, StageEnteredAt, now);
            StageEnteredAt = now;
        }
        else if (from == ApplicationStage.Hold && HeldFromStage == to)
        {
            // Resuming the same stage: its clock continues where it stopped, plus the time on hold.
            StageEnteredAt = HeldFromEnteredAt!.Value + (now - HeldAt!.Value);
            (HeldFromStage, HeldFromEnteredAt, HeldAt) = (null, null, null);
        }
        else
        {
            StageEnteredAt = now;
            (HeldFromStage, HeldFromEnteredAt, HeldAt) = (null, null, null);
        }

        Stage = to;
        TatBreachedAt = null;
        _stageHistory.Add(new StageMove(from, to, actor.Name, actor.Id, now));
        Raise(new StageChangedDomainEvent(this, from, to, actor, now, dwell));
        return Result.Success();
    }
}
