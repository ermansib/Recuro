using Recuro.BuildingBlocks.Domain;
using Recuro.Employee.Domain.Intake;

namespace Recuro.Employee.Domain.Ijp;

/// <summary>
/// RCU-EMP-002: an employee's application to an internal opening. It becomes a Pipeline application with
/// source IJP (abridged process, PPL-008). The grade and joining date are as the employee declared them;
/// HR-TA verifies them at screening.
/// </summary>
public sealed class InternalApplication : IntakeRecord
{
    private InternalApplication()
    {
    }

    public string PostingTitle { get; private set; } = string.Empty;

    public string DeclaredGrade { get; private set; } = string.Empty;

    public DateOnly JoinedOn { get; private set; }

    public static InternalApplication Start(
        string employeeId,
        string employeeName,
        IjpPosting posting,
        string declaredGrade,
        DateOnly joinedOn,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(posting);
        var application = new InternalApplication
        {
            EmployeeId = employeeId,
            EmployeeName = employeeName,
            ReqId = posting.ReqId,
            PostingTitle = posting.Title,
            DeclaredGrade = declaredGrade.Trim(),
            JoinedOn = joinedOn,
        };
        application.Begin(now);
        return application;
    }

    protected override void OnCompleted() => Raise(new InternalApplicationCompleted(this));
}

/// <summary>The saga finished: publish <c>employee.ijp.applied.v1</c> in the same transaction.</summary>
public sealed record InternalApplicationCompleted(InternalApplication Application) : IDomainEvent;
