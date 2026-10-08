using System.Globalization;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.Offer.Domain.Offers;

/// <summary>What HR-TA drafts, plus what the service looked up (requisition, candidate).</summary>
public sealed record OfferDraft(
    string AppId,
    string ReqId,
    string CandidateId,
    string CandidateName,
    string Designation,
    string Grade,
    string Location,
    string ReportingManager,
    DateOnly JoiningDate,
    int ProbationMonths,
    CtcComponents Components,
    PayBand Band);

/// <summary>When a sent offer is chased and when it lapses (RCU-OFR-006), worked out with the business calendar.</summary>
public sealed record SendPlan(DateTimeOffset FirstChaseAt, DateTimeOffset ExpiresAt, TimeSpan ChaseEvery, bool Conditional);

/// <summary>
/// An offer (S-12, RCU-OFR-*). The aggregate guards the FRD §6 state machine, the CTC trail (§9.8) and
/// the acceptance lifecycle; routing, rules and the BGV gate are resolved outside and passed in.
/// </summary>
public sealed class JobOffer : AggregateRoot, ITenantOwned
{
    private List<TrailEntry> _trail = [];

    private JobOffer()
    {
    }

    public Guid TenantId { get; private set; }

    public string AppId { get; private set; } = string.Empty;

    public string ReqId { get; private set; } = string.Empty;

    public string CandidateId { get; private set; } = string.Empty;

    /// <summary>Copied from the Candidate service for the letter; scrubbed on <c>candidate.purged</c>.</summary>
    public string CandidateName { get; private set; } = string.Empty;

    public string Designation { get; private set; } = string.Empty;

    public string Grade { get; private set; } = string.Empty;

    public string Location { get; private set; } = string.Empty;

    public string ReportingManager { get; private set; } = string.Empty;

    public DateOnly JoiningDate { get; private set; }

    public int ProbationMonths { get; private set; }

    public CtcComponents Components { get; private set; } = new(0, 0, 0);

    public PayBand Band { get; private set; } = new(0, 0);

    public OfferState State { get; private set; }

    /// <summary>0 until the first approval generates a letter; each later approval issues the next version.</summary>
    public int LetterVersion { get; private set; }

    /// <summary>The Annexure D route of the current submission.</summary>
    public OfferRoute? Route { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? NextChaseAt { get; private set; }

    public TimeSpan ChaseEvery { get; private set; }

    public int ChaseCount { get; private set; }

    /// <summary>Released under conditional-offer mode (BGV-009) before BGV cleared.</summary>
    public bool Conditional { get; private set; }

    public DateTimeOffset? RespondedAt { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public uint Version { get; private set; }

    /// <summary>Newest first, like the offer screen.</summary>
    public IReadOnlyList<TrailEntry> Trail => _trail;

    public bool IsOpen => !OfferTransitions.Terminal.Contains(State);

    public static JobOffer Create(OfferDraft draft, string by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var offer = new JobOffer
        {
            Id = Guid.CreateVersion7(),
            AppId = draft.AppId,
            ReqId = draft.ReqId,
            CandidateId = draft.CandidateId,
            CandidateName = draft.CandidateName,
            Designation = draft.Designation,
            Grade = draft.Grade,
            Location = draft.Location,
            ReportingManager = draft.ReportingManager,
            JoiningDate = draft.JoiningDate,
            ProbationMonths = draft.ProbationMonths,
            Components = draft.Components,
            Band = draft.Band,
            State = OfferState.Draft,
            CreatedBy = by,
            CreatedAt = now,
        };
        offer.Note(Format($"Offer drafted at {Lakhs(draft.Components.Total)}"), by, now, null, TrailKinds.Created);
        return offer;
    }

    /// <summary>
    /// RCU-OFR-005: a CTC revision writes the trail. A revision after submission voids that approval: the
    /// offer goes back to Draft and must be submitted again.
    /// </summary>
    public Result Revise(CtcComponents components, string? reason, string by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (!OfferTransitions.Editable.Contains(State))
        {
            return OfferErrors.NotEditable(State);
        }

        if (components == Components)
        {
            return Result.Success();
        }

        var before = Components.Total;
        Components = components;
        var because = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" · reason: {reason.Trim()}";
        Note(Format($"CTC revised to {Lakhs(components.Total)} (was {Lakhs(before)})"), $"{by}{because}", now, null, TrailKinds.Revision);
        if (State != OfferState.Draft)
        {
            BackToDraft();
        }

        return Result.Success();
    }

    /// <summary>RCU-OFR-005: a verbal offer, appended immutably.</summary>
    public Result LogVerbal(decimal value, string outcome, string? note, string by, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return OfferErrors.NotEditable(State);
        }

        var detail = string.IsNullOrWhiteSpace(note) ? by : $"{by} · {note.Trim()}";
        Note(Format($"Verbal offer at {Lakhs(value)} — {outcome}"), detail, now, null, TrailKinds.Verbal);
        return Result.Success();
    }

    /// <summary>RCU-OFR-002/003: send for approval on the resolved Annexure D route.</summary>
    public Result Submit(OfferRoute route, string by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(route);
        var move = OfferTransitions.Table.EnsureCanMove(State, OfferState.PendingApproval, "Offer");
        if (move.IsFailure)
        {
            return move;
        }

        State = OfferState.PendingApproval;
        Route = route;
        WorkflowInstanceId = null;
        Note($"Sent for approval — {route.Label}", by, now, null, TrailKinds.Submitted);
        Raise(new OfferSubmitted(this));
        return Result.Success();
    }

    public void AwaitApproval(Guid workflowInstanceId)
    {
        if (State != OfferState.PendingApproval)
        {
            throw new InvalidOperationException($"Offer {Id} is {State}, not waiting for approval.");
        }

        WorkflowInstanceId = workflowInstanceId;
    }

    /// <summary>
    /// RCU-OFR-003: the approval workflow finished. Approved issues the next letter version; declined
    /// returns the offer to HR-TA as a draft. A repeated outcome is a no-op (inbox and API hit one task).
    /// </summary>
    public Result ApplyDecision(bool approved, string by, string? reason, DateTimeOffset now)
    {
        if (approved && State == OfferState.Approved)
        {
            return Result.Success();
        }

        if (!approved && State == OfferState.Draft)
        {
            return Result.Success();
        }

        if (State != OfferState.PendingApproval)
        {
            return OfferErrors.NotPendingApproval(Id, State);
        }

        if (approved)
        {
            State = OfferState.Approved;
            LetterVersion++;
            Note(Format($"Approved by {by} at {Lakhs(Components.Total)}"), Format($"Annexure D · letter v{LetterVersion}"), now, true, TrailKinds.Decision);
            Raise(new OfferApproved(this, by));
        }
        else
        {
            BackToDraft();
            Note($"Returned to TA by {by}", string.IsNullOrWhiteSpace(reason) ? by : $"Reason: {reason.Trim()}", now, false, TrailKinds.Decision);
        }

        return Result.Success();
    }

    /// <summary>RCU-OFR-004/006/007: release the letter for e-sign. The caller has checked the BGV gate.</summary>
    public Result Send(SendPlan plan, string by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var move = OfferTransitions.Table.EnsureCanMove(State, OfferState.Sent, "Offer");
        if (move.IsFailure)
        {
            return move;
        }

        State = OfferState.Sent;
        SentAt = now;
        ExpiresAt = plan.ExpiresAt;
        NextChaseAt = plan.FirstChaseAt;
        ChaseEvery = plan.ChaseEvery;
        ChaseCount = 0;
        Conditional = plan.Conditional;
        var title = plan.Conditional
            ? "Conditional offer released with e-sign request (BGV pending)"
            : "Offer letter released with e-sign request";
        Note(title, Format($"{by} · valid until {ExpiresAt:yyyy-MM-dd}"), now, true, TrailKinds.Sent);
        Raise(new OfferSent(this));
        return Result.Success();
    }

    /// <summary>RCU-OFR-006: chase a sent offer once per period until it is answered or lapses.</summary>
    public bool RaiseChaseIfDue(DateTimeOffset now)
    {
        if (State != OfferState.Sent || NextChaseAt is not { } due || now < due || now >= ExpiresAt)
        {
            return false;
        }

        ChaseCount++;
        var next = due;
        while (next <= now && ChaseEvery > TimeSpan.Zero)
        {
            next += ChaseEvery;
        }

        NextChaseAt = ChaseEvery > TimeSpan.Zero ? next : null;
        Note(Format($"Acceptance chase #{ChaseCount} sent"), "system", now, null, TrailKinds.Chase);
        Raise(new OfferChaseDue(this));
        return true;
    }

    /// <summary>RCU-OFR-006: a sent offer lapses at the end of its validity.</summary>
    public bool ExpireIfDue(DateTimeOffset now)
    {
        if (State != OfferState.Sent || ExpiresAt is not { } expires || now < expires)
        {
            return false;
        }

        State = OfferState.Expired;
        NextChaseAt = null;
        Note("Offer expired unanswered", "system", now, false, TrailKinds.Response);
        Raise(new OfferExpired(this));
        return true;
    }

    public Result Accept(string by, DateTimeOffset now)
    {
        if (State == OfferState.Accepted)
        {
            return Result.Success();
        }

        var move = OfferTransitions.Table.EnsureCanMove(State, OfferState.Accepted, "Offer");
        if (move.IsFailure)
        {
            return move;
        }

        State = OfferState.Accepted;
        RespondedAt = now;
        NextChaseAt = null;
        Note("Candidate accepted the offer", by, now, true, TrailKinds.Response);
        Raise(new OfferAccepted(this));
        return Result.Success();
    }

    public Result Decline(string? reason, string by, DateTimeOffset now)
    {
        if (State == OfferState.Declined)
        {
            return Result.Success();
        }

        var move = OfferTransitions.Table.EnsureCanMove(State, OfferState.Declined, "Offer");
        if (move.IsFailure)
        {
            return move;
        }

        State = OfferState.Declined;
        RespondedAt = now;
        NextChaseAt = null;
        Note("Candidate declined the offer", string.IsNullOrWhiteSpace(reason) ? by : $"{by} · {reason.Trim()}", now, false, TrailKinds.Response);
        Raise(new OfferDeclined(this, reason));
        return Result.Success();
    }

    /// <summary>RCU-OFR-006: withdrawal (HR Head, or MD/CEO per §16) with a documented reason.</summary>
    public Result Withdraw(string? reason, string by, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < OfferLimits.MinReasonLength)
        {
            return OfferErrors.ReasonRequired();
        }

        var move = OfferTransitions.Table.EnsureCanMove(State, OfferState.Withdrawn, "Offer");
        if (move.IsFailure)
        {
            return move;
        }

        var from = State;
        State = OfferState.Withdrawn;
        NextChaseAt = null;
        Note($"Offer withdrawn by {by}", $"Reason: {reason.Trim()}", now, false, TrailKinds.Withdrawn);
        Raise(new OfferWithdrawn(this, from, reason.Trim()));
        return Result.Success();
    }

    /// <summary>An adverse BGV finding was flagged after release: recorded on the trail for the approvers.</summary>
    public void NoteBgv(string title, DateTimeOffset now) => Note(title, "BGV", now, null, TrailKinds.Bgv);

    /// <summary><c>candidate.purged</c>: the stored name goes; ids and amounts stay for the audit trail.</summary>
    public void ScrubCandidate() => CandidateName = string.Empty;

    private void BackToDraft()
    {
        State = OfferState.Draft;
        Route = null;
        WorkflowInstanceId = null;
    }

    private void Note(string title, string detail, DateTimeOffset at, bool? approved, string kind) =>
        _trail.Insert(0, new TrailEntry(title, detail, approved, at, kind));

    private static string Lakhs(decimal value) => Format($"₹{value:0.0}L");

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
