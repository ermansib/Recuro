using Recuro.BuildingBlocks.Domain;

namespace Recuro.Employee.Domain.Ijp;

/// <summary>What HR-TA writes about an internal opening, including who may apply (RCU-EMP-002: grade band, tenure).</summary>
public sealed record IjpPostingContent(
    string Title,
    string Location,
    string Department,
    string Grade,
    IReadOnlyList<string> EligibleGrades,
    int MinTenureMonths,
    string? Summary);

/// <summary>
/// RCU-EMP-001: an internal job posting. It is listed only while its IJP window is open: from the moment
/// sourcing was unlocked until the end of the window's last working day (FRD §9.2.1), enforced here and
/// not in the UI.
/// </summary>
public sealed class IjpPosting : AggregateRoot, ITenantOwned
{
    private readonly List<string> _eligibleGrades = [];

    private IjpPosting()
    {
    }

    public Guid TenantId { get; private set; }

    public string ReqId { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Location { get; private set; } = string.Empty;

    public string Department { get; private set; } = string.Empty;

    public string Grade { get; private set; } = string.Empty;

    /// <summary>Grades that may apply. Empty means any grade.</summary>
    public IReadOnlyList<string> EligibleGrades => _eligibleGrades;

    public int MinTenureMonths { get; private set; }

    public string? Summary { get; private set; }

    public DateTimeOffset OpensAt { get; private set; }

    public DateTimeOffset ClosesAt { get; private set; }

    /// <summary>The requisition was cancelled: delisted at once.</summary>
    public bool Withdrawn { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; private set; }

    public static IjpPosting Open(string reqId, IjpPostingContent content, DateTimeOffset opensAt, DateTimeOffset closesAt, string createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);
        var posting = new IjpPosting
        {
            Id = Guid.CreateVersion7(),
            ReqId = reqId,
            OpensAt = opensAt,
            ClosesAt = closesAt,
            CreatedBy = createdBy,
        };
        posting.Apply(content, now);
        return posting;
    }

    public bool IsListedAt(DateTimeOffset now) => !Withdrawn && OpensAt <= now && now <= ClosesAt;

    /// <summary>Content can change; the window cannot (fairness, §9.2.1).</summary>
    public Result Edit(IjpPostingContent content, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (Withdrawn)
        {
            return IjpErrors.Withdrawn(ReqId);
        }

        Apply(content, now);
        return Result.Success();
    }

    public void Withdraw(DateTimeOffset now) => (Withdrawn, UpdatedAt) = (true, now);

    /// <summary>
    /// RCU-EMP-002: may this employee apply? The grade must be in the band (when one is set) and tenure
    /// at least the minimum. Errors are per field so the form can show them.
    /// </summary>
    public Result CheckEligibility(string grade, DateOnly joinedOn, DateOnly today)
    {
        var errors = new List<FieldError>();
        if (_eligibleGrades.Count > 0 && !_eligibleGrades.Contains(grade.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new FieldError("currentGrade", "grade_not_eligible", $"This opening is for grades {string.Join(", ", _eligibleGrades)}."));
        }

        if (joinedOn > today)
        {
            errors.Add(new FieldError("joinedOn", "invalid_date", "The joining date cannot be in the future."));
        }
        else if (TenureMonths(joinedOn, today) < MinTenureMonths)
        {
            errors.Add(new FieldError("joinedOn", "tenure_too_short", $"This opening needs at least {MinTenureMonths} months in the company."));
        }

        return errors.Count == 0 ? Result.Success() : Error.Validation(errors);
    }

    /// <summary>Whole months between the two dates.</summary>
    public static int TenureMonths(DateOnly joinedOn, DateOnly today)
    {
        var months = ((today.Year - joinedOn.Year) * 12) + today.Month - joinedOn.Month;
        return today.Day < joinedOn.Day ? months - 1 : months;
    }

    private void Apply(IjpPostingContent content, DateTimeOffset now)
    {
        Title = content.Title.Trim();
        Location = content.Location.Trim();
        Department = content.Department.Trim();
        Grade = content.Grade.Trim();
        _eligibleGrades.Clear();
        _eligibleGrades.AddRange(content.EligibleGrades.Select(g => g.Trim()).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
        MinTenureMonths = content.MinTenureMonths;
        Summary = string.IsNullOrWhiteSpace(content.Summary) ? null : content.Summary.Trim();
        UpdatedAt = now;
    }
}

public static class IjpErrors
{
    public static Error NotFound(string reqId) => Error.NotFound("ijp_posting_not_found", $"There is no internal posting for {reqId}.");

    public static Error Withdrawn(string reqId) => Error.Conflict("ijp_withdrawn", $"The internal posting for {reqId} was withdrawn.");

    public static Error SourcingLocked(string reqId) =>
        Error.Conflict("sourcing_locked", $"{reqId} is not open for sourcing yet. Post it internally once the requisition is approved.");

    public static Error WindowClosed(string reqId) =>
        Error.Conflict("ijp_window_closed", $"The internal application window for {reqId} has closed.");

    public static Error AlreadyApplied(string reqId) =>
        Error.Conflict("already_applied", $"You already have an application in progress for {reqId}.");
}
