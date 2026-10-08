using Recuro.BuildingBlocks.Domain;

namespace Recuro.Onboarding.Domain.Cases;

/// <summary>Who acted on a case: a person, or a service reacting to an event.</summary>
public sealed record CaseActor(string? Id, string Name, string? Role);

/// <summary>RCU-ONB-002: one Annexure E item, with who ticked it and when.</summary>
public sealed class ChecklistItem
{
    private ChecklistItem()
    {
    }

    public string Key { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;

    public int Position { get; private set; }

    public bool Done { get; private set; }

    public string Remarks { get; private set; } = string.Empty;

    public DateTimeOffset? UpdatedAt { get; private set; }

    public string? UpdatedBy { get; private set; }

    internal static ChecklistItem Plan(string key, string label, int position) => new() { Key = key, Label = label, Position = position };

    internal void Set(bool done, string? remarks, CaseActor actor, DateTimeOffset now)
    {
        Done = done;
        if (remarks is not null)
        {
            Remarks = remarks.Trim();
        }

        UpdatedAt = now;
        UpdatedBy = actor.Name;
    }
}

/// <summary>RCU-ONB-003: one §13 document, its stored file and HR Ops' verification.</summary>
public sealed class CaseDocument
{
    private CaseDocument()
    {
    }

    public string Type { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;

    public bool Mandatory { get; private set; }

    public int Position { get; private set; }

    public DocumentStatus Status { get; private set; }

    public string? FileName { get; private set; }

    public string? ContentType { get; private set; }

    public long? Size { get; private set; }

    /// <summary>Where the file sits in the document store. Never shown to clients.</summary>
    public string? StorageKey { get; private set; }

    public DateTimeOffset? UploadedAt { get; private set; }

    public string? UploadedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public string? ReviewedBy { get; private set; }

    public string Note { get; private set; } = string.Empty;

    /// <summary>Counts toward a complete file only when verified.</summary>
    public bool IsVerified => Status == DocumentStatus.Verified;

    internal static CaseDocument Plan(string type, string label, bool mandatory, int position) =>
        new() { Type = type, Label = label, Mandatory = mandatory, Position = position, Status = DocumentStatus.Missing };

    internal void Upload(StoredFile file, CaseActor actor, DateTimeOffset now)
    {
        FileName = file.FileName;
        ContentType = file.ContentType;
        Size = file.Size;
        StorageKey = file.StorageKey;
        UploadedAt = now;
        UploadedBy = actor.Name;
        Status = DocumentStatus.Uploaded;
        ReviewedAt = null;
        ReviewedBy = null;
        Note = string.Empty;
    }

    internal void Review(bool verified, string? note, CaseActor actor, DateTimeOffset now)
    {
        Status = verified ? DocumentStatus.Verified : DocumentStatus.Rejected;
        Note = note?.Trim() ?? string.Empty;
        ReviewedAt = now;
        ReviewedBy = actor.Name;
    }
}

/// <summary>A file already written to the document store, ready to attach to a document slot.</summary>
public sealed record StoredFile(string FileName, string ContentType, long Size, string StorageKey);

/// <summary>A §9.9 or §9.10 step with its due date, reminders and completion notes.</summary>
public sealed class Milestone
{
    private Milestone()
    {
    }

    public Guid Id { get; private set; }

    public string Kind { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;

    public MilestonePhase Phase { get; private set; }

    /// <summary>Probation cycle: 1, then one more for each extension (RCU-ONB-005).</summary>
    public int Cycle { get; private set; }

    public DateOnly DueOn { get; private set; }

    public MilestoneStatus Status { get; private set; }

    public DateTimeOffset? RaisedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? CompletedBy { get; private set; }

    public string Notes { get; private set; } = string.Empty;

    /// <summary>The IT/Admin ticket the provisioning adapter opened (RCU-ONB-001).</summary>
    public string? TicketRef { get; private set; }

    public bool IsOpen => Status is MilestoneStatus.Scheduled or MilestoneStatus.Due;

    internal static Milestone Plan(string kind, string label, MilestonePhase phase, int cycle, DateOnly dueOn) => new()
    {
        Id = Guid.CreateVersion7(),
        Kind = kind,
        Label = label,
        Phase = phase,
        Cycle = cycle,
        DueOn = dueOn,
        Status = MilestoneStatus.Scheduled,
    };

    internal Result Raise(DateTimeOffset now)
    {
        var moved = MilestoneTransitions.Table.EnsureCanMove(Status, MilestoneStatus.Due, "Milestone");
        if (moved.IsSuccess)
        {
            Status = MilestoneStatus.Due;
            RaisedAt = now;
        }

        return moved;
    }

    internal Result Complete(string? notes, string? ticketRef, CaseActor actor, DateTimeOffset now)
    {
        var moved = MilestoneTransitions.Table.EnsureCanMove(Status, MilestoneStatus.Done, "Milestone");
        if (moved.IsFailure)
        {
            return moved;
        }

        Status = MilestoneStatus.Done;
        CompletedAt = now;
        CompletedBy = actor.Name;
        Notes = notes?.Trim() ?? string.Empty;
        TicketRef = ticketRef ?? TicketRef;
        return Result.Success();
    }

    internal void Cancel()
    {
        if (IsOpen)
        {
            Status = MilestoneStatus.Cancelled;
        }
    }
}

/// <summary>RCU-ONB-005: one recorded probation decision. Appended, never changed.</summary>
public sealed class ProbationDecision
{
    private ProbationDecision()
    {
    }

    public int Cycle { get; private set; }

    public ProbationOutcome Outcome { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    /// <summary>The extension's length; null on a confirmation.</summary>
    public int? ExtendedByMonths { get; private set; }

    public DateOnly? NewProbationEnd { get; private set; }

    public string DecidedBy { get; private set; } = string.Empty;

    public string? DecidedById { get; private set; }

    public DateTimeOffset DecidedAt { get; private set; }

    internal static ProbationDecision Record(int cycle, ProbationOutcome outcome, string reason, int? months, DateOnly? newEnd, CaseActor actor, DateTimeOffset now) => new()
    {
        Cycle = cycle,
        Outcome = outcome,
        Reason = reason,
        ExtendedByMonths = months,
        NewProbationEnd = newEnd,
        DecidedBy = actor.Name,
        DecidedById = actor.Id,
        DecidedAt = now,
    };
}
