using Recuro.BuildingBlocks.Domain;

namespace Recuro.Bgv.Domain.Cases;

/// <summary>RCU-BGV-001: the candidate's recorded authorisation for verification.</summary>
public sealed record Consent(DateTimeOffset At, string TextVersion, string Source);

/// <summary>Who acted on a case: a person, or a service reacting to an event.</summary>
public sealed record CaseActor(string? Id, string Name, string? Role);

/// <summary>The open adverse finding (frontend <c>BgvCase.adverse</c>) and the escalation carrying it.</summary>
public sealed record AdverseFinding(string Check, string Description, AdverseAction Action, DateTimeOffset FlaggedAt, string FlaggedBy, Guid? WorkflowId);

/// <summary>RCU-BGV-006: the mandatory, documented final decision on an adverse finding.</summary>
public sealed record AdverseResolution(AdverseOutcome Outcome, string Reason, string DecidedBy, DateTimeOffset DecidedAt);

/// <summary>A check that still blocks the offer release (RCU-BGV-005/007).</summary>
public sealed record GateBlocker(string Type, string Label, CheckStatus Status);

/// <summary>
/// A background-verification case for one application (FRD §9.7). The aggregate owns its checks and the
/// release gate: it publishes <c>bgv.cleared</c> exactly once, when consent is on file and every
/// applicable check is cleared, and it keeps the offer locked while an adverse finding is under review.
/// </summary>
public sealed class BgvCase : AggregateRoot, ITenantOwned
{
    private readonly List<BgvCheck> _checks = [];

    private BgvCase()
    {
    }

    public Guid TenantId { get; private set; }

    public string AppId { get; private set; } = string.Empty;

    public string ReqId { get; private set; } = string.Empty;

    public string CandidateId { get; private set; } = string.Empty;

    public string VendorId { get; private set; } = string.Empty;

    /// <summary>The vendor's name when the case was assigned, shown on the tracker.</summary>
    public string VendorName { get; private set; } = string.Empty;

    /// <summary>The vendor's own case reference; empty until dispatched.</summary>
    public string VendorCaseRef { get; private set; } = string.Empty;

    public Consent Consent { get; private set; } = null!;

    public string Grade { get; private set; } = string.Empty;

    public string[] RoleFlags { get; private set; } = [];

    public CheckScope Scope { get; private set; }

    /// <summary>The Config BGV matrix version the checks were planned from (pinned, RCU-BGV-002).</summary>
    public string MatrixVersionId { get; private set; } = string.Empty;

    public DateTimeOffset InitiatedAt { get; private set; }

    public string InitiatedBy { get; private set; } = string.Empty;

    /// <summary>RCU-BGV-005: the case TAT in working days (top of the §5.2 range) and when it runs out.</summary>
    public int TatWorkingDays { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public CaseStatus Status { get; private set; }

    public IReadOnlyList<BgvCheck> Checks => _checks;

    public AdverseFinding? Adverse { get; private set; }

    public AdverseResolution? Resolution { get; private set; }

    public DateTimeOffset? ClearedAt { get; private set; }

    /// <summary>RCU-VND-003: the vendor was de-empanelled while this case was open.</summary>
    public bool NeedsReassignment { get; private set; }

    public bool IsClosed => Status is CaseStatus.Cleared or CaseStatus.Rescinded or CaseStatus.Cancelled;

    public static BgvCase Initiate(
        string appId,
        string reqId,
        string candidateId,
        VendorRef vendor,
        string vendorCaseRef,
        Consent consent,
        RoleProfile role,
        string matrixVersionId,
        IReadOnlyList<PlannedCheck> checks,
        int tatWorkingDays,
        DateTimeOffset dueAt,
        CaseActor actor,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentNullException.ThrowIfNull(vendor);
        ArgumentNullException.ThrowIfNull(consent);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(actor);

        var bgvCase = new BgvCase
        {
            Id = Guid.CreateVersion7(),
            AppId = appId,
            ReqId = reqId,
            CandidateId = candidateId,
            VendorId = vendor.Id,
            VendorName = vendor.Name,
            VendorCaseRef = vendorCaseRef.Trim(),
            Consent = consent,
            Grade = role.Grade,
            RoleFlags = [.. role.Flags.Order(StringComparer.Ordinal)],
            Scope = role.Scope,
            MatrixVersionId = matrixVersionId,
            InitiatedAt = now,
            InitiatedBy = actor.Name,
            TatWorkingDays = tatWorkingDays,
            DueAt = dueAt,
            Status = CaseStatus.Open,
        };
        bgvCase._checks.AddRange(checks.Select(BgvCheck.Plan));
        bgvCase.Raise(new CaseInitiatedDomainEvent(bgvCase));

        // Nothing applicable (e.g. a delta-only scope): the gate opens at once.
        bgvCase.TryClear(now);
        return bgvCase;
    }

    /// <summary>
    /// RCU-BGV-003 (manual path until vendor webhooks): a check moves along §6.3. Flagging goes through
    /// <see cref="ReportAdverse"/> because it needs a finding and an escalation.
    /// </summary>
    public Result UpdateCheck(string type, CheckStatus to, string? note, string? sensitiveNote, CaseActor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (to == CheckStatus.Flagged)
        {
            return BgvErrors.UseAdverseReport;
        }

        if (IsClosed)
        {
            return BgvErrors.Closed(Status);
        }

        var check = FindCheck(type);
        if (check is null)
        {
            return BgvErrors.CheckNotFound(type);
        }

        if (to == CheckStatus.Cleared && Status == CaseStatus.UnderReview && check.Status == CheckStatus.Flagged)
        {
            return BgvErrors.UnderReview;
        }

        var from = check.Status;
        var moved = check.MoveTo(to, note, sensitiveNote, DateOnly.FromDateTime(now.UtcDateTime));
        if (moved.IsFailure)
        {
            return moved;
        }

        Raise(new CheckUpdatedDomainEvent(this, check.Type, from, to, actor, now));
        TryClear(now);
        return Result.Success();
    }

    /// <summary>
    /// RCU-BGV-006: flags a check, locks the offer release and starts the adverse saga. A pending check
    /// counts as started: it moves to In Progress first, so the history follows §6.3.
    /// </summary>
    public Result ReportAdverse(string type, string description, AdverseAction action, CaseActor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(description))
        {
            return BgvErrors.DescriptionRequired;
        }

        if (Status == CaseStatus.UnderReview)
        {
            return BgvErrors.AdverseAlreadyOpen;
        }

        if (IsClosed)
        {
            return BgvErrors.Closed(Status);
        }

        var check = FindCheck(type);
        if (check is null)
        {
            return BgvErrors.CheckNotFound(type);
        }

        if (CheckTransitions.IsSettled(check.Status))
        {
            return BgvErrors.CannotFlag(check.Label, check.Status);
        }

        var finding = description.Trim();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (check.Status == CheckStatus.Pending)
        {
            check.MoveTo(CheckStatus.InProgress, null, null, today);
            Raise(new CheckUpdatedDomainEvent(this, check.Type, CheckStatus.Pending, CheckStatus.InProgress, actor, now));
        }

        var from = check.Status;
        var flagged = check.MoveTo(CheckStatus.Flagged, finding, null, today);
        if (flagged.IsFailure)
        {
            return flagged;
        }

        Status = CaseStatus.UnderReview;
        Adverse = new AdverseFinding(check.Type, finding, action, now, actor.Name, null);
        Resolution = null;
        Raise(new CheckUpdatedDomainEvent(this, check.Type, from, CheckStatus.Flagged, actor, now));
        Raise(new AdverseFlaggedDomainEvent(this, check.Type, action));
        return Result.Success();
    }

    /// <summary>Links the open finding to the workflow that escalates it (HR Head + Compliance → MD/CEO).</summary>
    public void AttachEscalation(Guid workflowId)
    {
        if (Adverse is not null)
        {
            Adverse = Adverse with { WorkflowId = workflowId };
        }
    }

    /// <summary>
    /// RCU-BGV-006: the final decision closes the saga. Override clears the flagged check (with the
    /// rationale as its note) and the case carries on; rescind closes the case adverse.
    /// </summary>
    public Result Resolve(AdverseOutcome outcome, string reason, CaseActor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (Status != CaseStatus.UnderReview || Adverse is null)
        {
            return BgvErrors.NothingToResolve;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return BgvErrors.ReasonRequired;
        }

        var rationale = reason.Trim();
        Resolution = new AdverseResolution(outcome, rationale, actor.Name, now);
        if (outcome == AdverseOutcome.Override)
        {
            var check = FindCheck(Adverse.Check)!;
            check.MoveTo(CheckStatus.Cleared, $"Override — {rationale}", null, DateOnly.FromDateTime(now.UtcDateTime));
            Status = CaseStatus.Open;
            Raise(new CheckUpdatedDomainEvent(this, check.Type, CheckStatus.Flagged, CheckStatus.Cleared, actor, now));
        }
        else
        {
            Status = CaseStatus.Rescinded;
        }

        Raise(new AdverseResolvedDomainEvent(this, outcome, actor));
        TryClear(now);
        return Result.Success();
    }

    /// <summary>RCU-BGV-007: what still blocks the offer. Empty only when the case has cleared.</summary>
    public IReadOnlyList<GateBlocker> ReleaseBlockers() =>
        _checks.Where(c => !CheckTransitions.IsSettled(c.Status)).Select(c => new GateBlocker(c.Type, c.Label, c.Status)).ToList();

    /// <summary>RCU-BGV-005: success only when the gate is open; otherwise 409 listing every blocking check.</summary>
    public Result EnsureReleasable()
    {
        if (Status == CaseStatus.Cleared)
        {
            return Result.Success();
        }

        return Status == CaseStatus.Rescinded ? BgvErrors.Rescinded : BgvErrors.ReleaseBlocked(Status, ReleaseBlockers());
    }

    /// <summary>The application left the BGV stage for good (rejected or withdrawn).</summary>
    public void Cancel()
    {
        if (!IsClosed)
        {
            Status = CaseStatus.Cancelled;
        }
    }

    /// <summary>RCU-VND-003: the vendor was de-empanelled; open cases wait for another vendor.</summary>
    public void FlagForReassignment()
    {
        if (!IsClosed)
        {
            NeedsReassignment = true;
        }
    }

    /// <summary>RCU-VND-003: hands an open case to another active vendor. The new vendor's reference comes with dispatch.</summary>
    public Result Reassign(VendorRef vendor)
    {
        ArgumentNullException.ThrowIfNull(vendor);
        if (IsClosed)
        {
            return BgvErrors.Closed(Status);
        }

        VendorId = vendor.Id;
        VendorName = vendor.Name;
        VendorCaseRef = string.Empty;
        NeedsReassignment = false;
        return Result.Success();
    }

    /// <summary>Working days elapsed since initiation, for the tracker's TAT counter.</summary>
    public int TatDay(DateTimeOffset now) =>
        WorkingDays.Between(DateOnly.FromDateTime(InitiatedAt.UtcDateTime), DateOnly.FromDateTime((ClearedAt ?? now).UtcDateTime));

    private BgvCheck? FindCheck(string type) => _checks.Find(c => string.Equals(c.Type, type, StringComparison.Ordinal));

    private void TryClear(DateTimeOffset now)
    {
        if (Status != CaseStatus.Open || ClearedAt is not null || _checks.Any(c => !CheckTransitions.IsSettled(c.Status)))
        {
            return;
        }

        Status = CaseStatus.Cleared;
        ClearedAt = now;
        Raise(new CaseClearedDomainEvent(this, now <= DueAt));
    }
}

/// <summary>An empanelled vendor as the Vendor service reported it.</summary>
public sealed record VendorRef(string Id, string Name);
