using Recuro.BuildingBlocks.Domain;
using Recuro.Onboarding.Domain.Rules;

namespace Recuro.Onboarding.Domain.Cases;

/// <summary>The accepted offer that opens a case (from <c>offer.accepted.v1</c>).</summary>
public sealed record AcceptedOffer(string AppId, string? OfferId, string ReqId, string CandidateId, DateOnly JoiningDate, int? ProbationMonths);

/// <summary>
/// One hire's journey from offer acceptance to confirmation (FRD §9.9–9.10, data model
/// <c>OnboardingCase</c>): pre-boarding touchpoints, the Annexure E Day-1 checklist, the §13 document
/// file and the probation milestones. The aggregate owns the rules: Day-1 Ready fires once, at 100%;
/// confirmation needs a complete file, a cleared BGV and the end of probation.
/// </summary>
public sealed class OnboardingCase : AggregateRoot, ITenantOwned
{
    private readonly List<ChecklistItem> _checklist = [];
    private readonly List<CaseDocument> _documents = [];
    private readonly List<Milestone> _milestones = [];
    private readonly List<ProbationDecision> _decisions = [];

    /// <summary>Shortest and longest single probation extension (§9.10).</summary>
    public const int MinExtensionMonths = 1;
    public const int MaxExtensionMonths = 6;

    private OnboardingCase()
    {
    }

    public Guid TenantId { get; private set; }

    public string AppId { get; private set; } = string.Empty;

    public string? OfferId { get; private set; }

    public string ReqId { get; private set; } = string.Empty;

    public string CandidateId { get; private set; } = string.Empty;

    public DateOnly JoiningDate { get; private set; }

    public DateTimeOffset AcceptedAt { get; private set; }

    public CaseStatus Status { get; private set; }

    /// <summary>The onboarding rules version the case was planned from (pinned, like every rules consumer).</summary>
    public string RulesVersionId { get; private set; } = string.Empty;

    public int ProbationMonths { get; private set; }

    public DateOnly ProbationEndsOn { get; private set; }

    public int ProbationCycle { get; private set; }

    public string? ReportingManagerId { get; private set; }

    public string? ReportingManager { get; private set; }

    public string? Buddy { get; private set; }

    /// <summary>
    /// The joiner's department key (tenant data, e.g. <c>operations</c>). Its head decides probation
    /// (RCU-ONB-005). Unset means the reporting manager's department applies.
    /// </summary>
    public string? Department { get; private set; }

    public DateTimeOffset? Day1ReadyAt { get; private set; }

    public DateTimeOffset? FileCompletedAt { get; private set; }

    public string? FileCompletedBy { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancelReason { get; private set; }

    public IReadOnlyList<ChecklistItem> Checklist => _checklist;

    public IReadOnlyList<CaseDocument> Documents => _documents;

    public IReadOnlyList<Milestone> Milestones => _milestones;

    public IReadOnlyList<ProbationDecision> Decisions => _decisions;

    public bool IsClosed => CaseTransitions.IsClosed(Status);

    /// <summary>Day-1 completion, 0–100 (the prototype's ring).</summary>
    public int ChecklistPercent => _checklist.Count == 0 ? 100 : (int)Math.Round(_checklist.Count(i => i.Done) * 100.0 / _checklist.Count, MidpointRounding.AwayFromZero);

    /// <summary>
    /// RCU-ONB-001: opens the case on acceptance. The joining instructions count as sent now; the
    /// touchpoints and the IT/Admin ticket wait for their dates; the §9.10 probation cycle is planned
    /// from the joining date.
    /// </summary>
    public static OnboardingCase Start(AcceptedOffer offer, OnboardingTemplate template, IReadOnlyList<PlannedMilestone> preBoarding, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(preBoarding);
        ArgumentException.ThrowIfNullOrWhiteSpace(offer.AppId);

        var months = offer.ProbationMonths is > 0 ? offer.ProbationMonths.Value : template.ProbationMonths;
        var onboardingCase = new OnboardingCase
        {
            Id = Guid.CreateVersion7(),
            AppId = offer.AppId,
            OfferId = offer.OfferId,
            ReqId = offer.ReqId,
            CandidateId = offer.CandidateId,
            JoiningDate = offer.JoiningDate,
            AcceptedAt = now,
            Status = CaseStatus.PreBoarding,
            RulesVersionId = template.VersionId,
            ProbationMonths = months,
            ProbationEndsOn = offer.JoiningDate.AddMonths(months),
            ProbationCycle = 1,
        };

        onboardingCase._checklist.AddRange(template.Checklist.Select((item, i) => ChecklistItem.Plan(item.Key, item.Label, i)));
        onboardingCase._documents.AddRange(template.Documents.Select((doc, i) => CaseDocument.Plan(doc.Type, doc.Label, doc.Mandatory, i)));

        var instructions = Milestone.Plan(MilestoneKinds.JoiningInstructions, "Joining instructions & document list emailed", MilestonePhase.PreBoarding, 1, DateOnly.FromDateTime(now.UtcDateTime));
        instructions.Complete(null, null, new CaseActor(null, "Onboarding service", null), now);
        onboardingCase._milestones.Add(instructions);
        onboardingCase._milestones.AddRange(preBoarding.Select(p => Milestone.Plan(p.Kind, p.Label, p.Phase, 1, p.DueOn)));
        onboardingCase._milestones.AddRange(template
            .ProbationMilestones(offer.JoiningDate, onboardingCase.ProbationEndsOn)
            .Select(p => Milestone.Plan(p.Kind, p.Label, p.Phase, 1, p.DueOn)));

        onboardingCase.Raise(new PreBoardingStartedDomainEvent(onboardingCase, now));
        return onboardingCase;
    }

    /// <summary>RCU-ONB-002: ticks or unticks one item. At 100% the record becomes Day-1 Ready, once.</summary>
    public Result SetChecklistItem(string key, bool done, string? remarks, CaseActor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (Status == CaseStatus.Day1Ready)
        {
            return OnboardingErrors.ChecklistLocked;
        }

        if (IsClosed)
        {
            return OnboardingErrors.Closed(Status);
        }

        var item = _checklist.Find(i => string.Equals(i.Key, key, StringComparison.Ordinal));
        if (item is null)
        {
            return OnboardingErrors.ItemNotFound(key);
        }

        item.Set(done, remarks, actor, now);
        if (_checklist.TrueForAll(i => i.Done))
        {
            Status = CaseStatus.Day1Ready;
            Day1ReadyAt = now;
            Raise(new Day1ReadyDomainEvent(this, actor, now));
        }

        return Result.Success();
    }

    /// <summary>
    /// The reporting manager, buddy (Annexure E's last item) and department; milestone reminders go to
    /// the manager and the probation decision to the department's head.
    /// </summary>
    public Result Assign(string? reportingManagerId, string? reportingManager, string? buddy, string? department = null)
    {
        if (IsClosed)
        {
            return OnboardingErrors.Closed(Status);
        }

        ReportingManagerId = Clean(reportingManagerId);
        ReportingManager = Clean(reportingManager);
        Buddy = Clean(buddy);
        Department = Clean(department)?.ToLowerInvariant();
        return Result.Success();
    }

    /// <summary>
    /// RCU-ONB-003: attaches an uploaded file to a document slot, waiting for verification. Returns the
    /// replaced file's storage key, if any, so the caller can delete it. A new file reopens the file check.
    /// </summary>
    public Result<string?> AttachDocument(string type, StoredFile file, CaseActor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(actor);
        if (IsClosed)
        {
            return OnboardingErrors.Closed(Status);
        }

        var document = FindDocument(type);
        if (document is null)
        {
            return OnboardingErrors.DocumentNotFound(type);
        }

        var replaced = document.StorageKey;
        document.Upload(file, actor, now);
        if (document.Mandatory)
        {
            FileCompletedAt = null;
            FileCompletedBy = null;
        }

        return replaced;
    }

    /// <summary>RCU-ONB-003: HR Ops verifies or rejects an uploaded document. A rejection says why.</summary>
    public Result ReviewDocument(string type, bool verified, string? note, CaseActor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (IsClosed)
        {
            return OnboardingErrors.Closed(Status);
        }

        var document = FindDocument(type);
        if (document is null)
        {
            return OnboardingErrors.DocumentNotFound(type);
        }

        if (document.Status is DocumentStatus.Missing)
        {
            return OnboardingErrors.NothingToReview;
        }

        if (!verified && string.IsNullOrWhiteSpace(note))
        {
            return OnboardingErrors.RejectionNoteRequired;
        }

        document.Review(verified, note, actor, now);
        if (!verified && document.Mandatory)
        {
            FileCompletedAt = null;
            FileCompletedBy = null;
        }

        return Result.Success();
    }

    /// <summary>RCU-ONB-003: every mandatory document that is not verified yet.</summary>
    public IReadOnlyList<MissingDocument> MissingDocuments() =>
        _documents.Where(d => d.Mandatory && !d.IsVerified).Select(d => new MissingDocument(d.Type, d.Label, d.Status)).ToList();

    /// <summary>RCU-ONB-003: marks the file complete, or 409 listing what is missing.</summary>
    public Result CompleteFile(CaseActor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (Status == CaseStatus.Cancelled)
        {
            return OnboardingErrors.Closed(Status);
        }

        var missing = MissingDocuments();
        if (missing.Count > 0)
        {
            return OnboardingErrors.FileIncomplete(missing);
        }

        if (FileCompletedAt is null)
        {
            FileCompletedAt = now;
            FileCompletedBy = actor.Name;
        }

        return Result.Success();
    }

    /// <summary>
    /// RCU-ONB-001/004: raises every scheduled milestone whose date has come (reminders go out through
    /// <c>onboarding.milestone.due</c>). The IT/Admin ticket is opened by the adapter instead.
    /// </summary>
    public IReadOnlyList<Milestone> RaiseDueMilestones(DateOnly today, DateTimeOffset now)
    {
        if (IsClosed)
        {
            return [];
        }

        var raised = new List<Milestone>();
        foreach (var milestone in _milestones.Where(m => m.Status == MilestoneStatus.Scheduled && m.DueOn <= today && m.Kind != MilestoneKinds.ItProvisioning))
        {
            if (milestone.Raise(now).IsSuccess)
            {
                raised.Add(milestone);
                Raise(new MilestoneDueDomainEvent(this, milestone));
            }
        }

        return raised;
    }

    /// <summary>The IT/Admin provisioning step when its date has come and no ticket exists yet.</summary>
    public Milestone? ProvisioningDue(DateOnly today) =>
        IsClosed ? null : _milestones.Find(m => m.Kind == MilestoneKinds.ItProvisioning && m.Status == MilestoneStatus.Scheduled && m.DueOn <= today);

    /// <summary>RCU-ONB-001: the adapter opened the IT/Admin ticket.</summary>
    public Result RecordProvisioning(Guid milestoneId, string ticketRef, DateTimeOffset now)
    {
        var milestone = _milestones.Find(m => m.Id == milestoneId);
        return milestone is null
            ? OnboardingErrors.MilestoneNotFound(milestoneId)
            : milestone.Complete($"Ticket {ticketRef} opened", ticketRef, new CaseActor(null, "IT/Admin provisioning adapter", null), now);
    }

    /// <summary>RCU-ONB-004: completes a touchpoint or probation milestone with notes. The end of probation needs a decision instead.</summary>
    public Result CompleteMilestone(Guid milestoneId, string? notes, CaseActor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (IsClosed)
        {
            return OnboardingErrors.Closed(Status);
        }

        var milestone = _milestones.Find(m => m.Id == milestoneId);
        if (milestone is null)
        {
            return OnboardingErrors.MilestoneNotFound(milestoneId);
        }

        return milestone.Kind == MilestoneKinds.ProbationEnd
            ? OnboardingErrors.UseProbationDecision
            : milestone.Complete(notes, null, actor, now);
    }

    /// <summary>
    /// RCU-ONB-005: the decision at the end of probation, recorded once per cycle and never changed.
    /// Confirm needs a complete file and a cleared BGV (RCU-ONB-003, BGV-009); Extend needs a reason
    /// and starts a new review cycle.
    /// </summary>
    public Result DecideProbation(ProbationOutcome outcome, string? reason, int? extendByMonths, BgvStatus bgv, CaseActor actor, DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (IsClosed)
        {
            return OnboardingErrors.Closed(Status);
        }

        if (Status != CaseStatus.Day1Ready)
        {
            return OnboardingErrors.NotJoined;
        }

        if (today < ProbationEndsOn)
        {
            return OnboardingErrors.ProbationNotEnded(ProbationEndsOn);
        }

        var end = _milestones.Find(m => m.Kind == MilestoneKinds.ProbationEnd && m.Cycle == ProbationCycle && m.IsOpen);
        var why = reason?.Trim() ?? string.Empty;
        if (outcome == ProbationOutcome.Confirm)
        {
            var missing = MissingDocuments();
            if (missing.Count > 0)
            {
                return OnboardingErrors.FileIncomplete(missing);
            }

            if (bgv != BgvStatus.Cleared)
            {
                return OnboardingErrors.BgvNotCleared(bgv);
            }

            var confirmation = ProbationDecision.Record(ProbationCycle, outcome, why, null, null, actor, now);
            _decisions.Add(confirmation);
            end?.Complete(why.Length > 0 ? $"Confirmed — {why}" : "Confirmed", null, actor, now);
            CancelOpenMilestones();
            Status = CaseStatus.Confirmed;
            ConfirmedAt = now;
            Raise(new EmployeeConfirmedDomainEvent(this, confirmation));
            return Result.Success();
        }

        if (why.Length == 0)
        {
            return OnboardingErrors.ReasonRequired;
        }

        if (extendByMonths is not (>= MinExtensionMonths and <= MaxExtensionMonths))
        {
            return OnboardingErrors.ExtensionLength;
        }

        var months = extendByMonths.Value;
        var cycleStart = ProbationEndsOn;
        var newEnd = cycleStart.AddMonths(months);
        var extension = ProbationDecision.Record(ProbationCycle, outcome, why, months, newEnd, actor, now);
        _decisions.Add(extension);
        end?.Complete($"Extended by {months} month(s) — {why}", null, actor, now);

        ProbationCycle++;
        ProbationEndsOn = newEnd;
        var reviewOn = cycleStart.AddDays((newEnd.DayNumber - cycleStart.DayNumber) / 2);
        _milestones.Add(Milestone.Plan(MilestoneKinds.Review, "Extended probation review", MilestonePhase.Probation, ProbationCycle, reviewOn));
        _milestones.Add(Milestone.Plan(MilestoneKinds.ProbationEnd, "End of extended probation: confirm or extend", MilestonePhase.Probation, ProbationCycle, newEnd));
        Raise(new ProbationExtendedDomainEvent(this, extension));
        return Result.Success();
    }

    /// <summary>
    /// Saga compensation (§6.3): the offer was withdrawn after acceptance. Stops every reminder and
    /// returns the IT/Admin tickets to cancel. A confirmed employee is no longer an offer matter.
    /// </summary>
    public IReadOnlyList<string> Cancel(string reason, DateTimeOffset now)
    {
        if (IsClosed)
        {
            return [];
        }

        CancelOpenMilestones();
        Status = CaseStatus.Cancelled;
        CancelledAt = now;
        CancelReason = reason;
        return _milestones.Where(m => m.Kind == MilestoneKinds.ItProvisioning && m.TicketRef is not null).Select(m => m.TicketRef!).ToList();
    }

    private void CancelOpenMilestones()
    {
        foreach (var milestone in _milestones)
        {
            milestone.Cancel();
        }
    }

    private CaseDocument? FindDocument(string type) => _documents.Find(d => string.Equals(d.Type, type, StringComparison.Ordinal));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
