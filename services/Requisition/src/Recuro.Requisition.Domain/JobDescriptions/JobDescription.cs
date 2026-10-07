using Recuro.BuildingBlocks.Domain;

namespace Recuro.Requisition.Domain.JobDescriptions;

public enum JobDescriptionStatus
{
    Draft,
    Submitted,
    Approved,
}

/// <summary>What a JD says. Saved as a draft, frozen into an immutable version on submit (RCU-JD-004).</summary>
public sealed record JobDescriptionContent(
    string Purpose,
    IReadOnlyList<string> Responsibilities,
    string ReportsTo,
    string TeamSize,
    string Location,
    string MinQualification,
    string Experience,
    string Grade,
    IReadOnlyList<string> Competencies,
    IReadOnlyList<string> Assessments,
    string Benchmark)
{
    /// <summary>Blank responsibilities are dropped, the rest trimmed (as the mock does).</summary>
    public JobDescriptionContent Normalised() => this with
    {
        Responsibilities = Responsibilities.Select(r => r.Trim()).Where(r => r.Length > 0).ToList(),
    };
}

/// <summary>One line of the JD's version history (frontend <c>JobDescription.history</c>).</summary>
public sealed record JobDescriptionRevision(int Version, string Note, string By, DateOnly At);

/// <summary>
/// The job description of one requisition (S-05 JD builder). It lives in the Requisition service because a
/// JD belongs to exactly one requisition and gates posting (CAR-007).
/// </summary>
public sealed class JobDescription : AggregateRoot, ITenantOwned
{
    public const int MinResponsibilities = 2;
    public const int MaxResponsibilities = 6;
    public const int MinCompetencies = 3;
    public const int MinAssessments = 1;

    private List<JobDescriptionRevision> _history = [];

    private JobDescription()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid RequisitionId { get; private set; }

    public string ReqId { get; private set; } = string.Empty;

    public int VersionNumber { get; private set; }

    public JobDescriptionStatus Status { get; private set; }

    public JobDescriptionContent Content { get; private set; } = null!;

    public IReadOnlyList<JobDescriptionRevision> History => _history;

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    /// <summary>The initial draft, seeded from the requisition's own fields when it is submitted.</summary>
    public static JobDescription CreateFromRequisition(Guid requisitionId, string reqId, JobDescriptionContent content, string by, DateOnly today) => new()
    {
        Id = Guid.CreateVersion7(),
        RequisitionId = requisitionId,
        ReqId = reqId,
        VersionNumber = 1,
        Status = JobDescriptionStatus.Draft,
        Content = content,
        _history = [new JobDescriptionRevision(1, "initial draft from MRF", by, today)],
    };

    /// <summary>RCU-JD-001..004: validates, then freezes the next version as Submitted.</summary>
    public Result Submit(JobDescriptionContent content, string by, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(content);
        var normalised = content.Normalised();
        var problems = Validate(normalised);
        if (problems.Count > 0)
        {
            return Error.Validation(problems);
        }

        Content = normalised;
        VersionNumber++;
        Status = JobDescriptionStatus.Submitted;
        _history = [new JobDescriptionRevision(VersionNumber, "submitted for grade benchmark & approval", by, today), .. _history];
        return Result.Success();
    }

    public static IReadOnlyList<FieldError> Validate(JobDescriptionContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var problems = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(content.Purpose))
        {
            problems.Add(new FieldError("purpose", "required", "Role purpose is required."));
        }

        var responsibilities = content.Responsibilities.Count(r => !string.IsNullOrWhiteSpace(r));
        if (responsibilities < MinResponsibilities)
        {
            problems.Add(new FieldError("responsibilities", "min", $"Add at least {MinResponsibilities} key responsibilities."));
        }
        else if (responsibilities > MaxResponsibilities)
        {
            problems.Add(new FieldError("responsibilities", "max", $"At most {MaxResponsibilities} key responsibilities."));
        }

        if (content.Competencies.Count < MinCompetencies)
        {
            problems.Add(new FieldError("competencies", "min", $"Select at least {MinCompetencies} competencies."));
        }

        if (content.Assessments.Count < MinAssessments)
        {
            problems.Add(new FieldError("assessments", "min", "Select at least one assessment."));
        }

        return problems;
    }
}
