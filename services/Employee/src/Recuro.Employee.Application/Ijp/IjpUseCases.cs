using FluentValidation;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Employee.Application.Abstractions;
using Recuro.Employee.Application.Referrals;
using Recuro.Employee.Domain.Ijp;

namespace Recuro.Employee.Application.Ijp;

/// <summary>An internal opening as employees see it, with the window's end so the UI can count down.</summary>
public sealed record IjpPostingDto(
    string ReqId,
    string Title,
    string Location,
    string Department,
    string Grade,
    IReadOnlyList<string> EligibleGrades,
    int MinTenureMonths,
    string? Summary,
    DateTimeOffset OpensAt,
    DateTimeOffset ClosesAt)
{
    public static IjpPostingDto From(IjpPosting posting)
    {
        ArgumentNullException.ThrowIfNull(posting);
        return new IjpPostingDto(
            posting.ReqId,
            posting.Title,
            posting.Location,
            posting.Department,
            posting.Grade,
            posting.EligibleGrades,
            posting.MinTenureMonths,
            posting.Summary,
            posting.OpensAt,
            posting.ClosesAt);
    }
}

/// <summary>RCU-EMP-005: one of the employee's own IJP applications, coarse status only.</summary>
public sealed record MyApplicationDto(string Id, string ReqId, string Title, string? AppId, string Status, string Progress, DateTimeOffset SubmittedAt);

/// <summary>RCU-EMP-001: HR-TA posts (or edits) an internal opening for a requisition that is open for sourcing.</summary>
public sealed record UpsertIjpPostingCommand(
    string ReqId,
    string Title,
    string Location,
    string Department,
    string Grade,
    IReadOnlyList<string> EligibleGrades,
    int MinTenureMonths,
    string? Summary) : ICommand<IjpPostingDto>;

internal sealed class UpsertIjpPostingCommandValidator : AbstractValidator<UpsertIjpPostingCommand>
{
    public UpsertIjpPostingCommandValidator()
    {
        RuleFor(c => c.ReqId).NotEmpty().MaximumLength(EmployeeLimits.ReqIdLength);
        RuleFor(c => c.Title).NotEmpty().MaximumLength(EmployeeLimits.TitleLength);
        RuleFor(c => c.Location).NotEmpty().MaximumLength(EmployeeLimits.ShortTextLength);
        RuleFor(c => c.Department).NotEmpty().MaximumLength(EmployeeLimits.ShortTextLength);
        RuleFor(c => c.Grade).NotEmpty().MaximumLength(EmployeeLimits.GradeLength);
        RuleFor(c => c.EligibleGrades).NotNull().Must(g => g.Count <= EmployeeLimits.MaxGrades);
        RuleForEach(c => c.EligibleGrades).NotEmpty().MaximumLength(EmployeeLimits.GradeLength);
        RuleFor(c => c.MinTenureMonths).InclusiveBetween(0, 240);
        RuleFor(c => c.Summary).MaximumLength(EmployeeLimits.SummaryLength);
    }
}

internal sealed class UpsertIjpPostingCommandHandler(
    IIjpPostingRepository postings,
    ISourcingGateRepository gates,
    IWorkingDayCalendar calendar,
    IOptions<EmployeeOptions> options,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UpsertIjpPostingCommand, IjpPostingDto>
{
    public async Task<Result<IjpPostingDto>> Handle(UpsertIjpPostingCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var content = new IjpPostingContent(
            command.Title, command.Location, command.Department, command.Grade, command.EligibleGrades, command.MinTenureMonths, command.Summary);
        var posting = await postings.GetAsync(command.ReqId, ct);
        if (posting is not null)
        {
            var edited = posting.Edit(content, now);
            if (edited.IsFailure)
            {
                return edited.Error!;
            }
        }
        else
        {
            // RCU-MRF-006: no internal posting before the requisition is approved for sourcing.
            var gate = await gates.GetAsync(command.ReqId, ct);
            if (gate is not { IsOpen: true, UnlockedAt: { } opensAt })
            {
                return IjpErrors.SourcingLocked(command.ReqId);
            }

            var closesAt = await IjpWindow.EndAsync(calendar, opensAt, options.Value.IjpWindowWorkingDays, ct);
            posting = IjpPosting.Open(command.ReqId, content, opensAt, closesAt, caller.Name ?? caller.UserId ?? "unknown", now);
            postings.Add(posting);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return IjpPostingDto.From(posting);
    }
}

/// <summary>RCU-EMP-001: internal openings whose window is open now. Closed windows are never listed.</summary>
public sealed record ListIjpPostingsQuery : IQuery<IReadOnlyList<IjpPostingDto>>;

internal sealed class ListIjpPostingsQueryHandler(IIjpPostingRepository postings, TimeProvider clock)
    : IQueryHandler<ListIjpPostingsQuery, IReadOnlyList<IjpPostingDto>>
{
    public async Task<Result<IReadOnlyList<IjpPostingDto>>> Handle(ListIjpPostingsQuery query, CancellationToken ct) =>
        Result.Success<IReadOnlyList<IjpPostingDto>>((await postings.ListOpenAsync(clock.GetUtcNow(), ct)).Select(IjpPostingDto.From).ToList());
}

/// <summary>
/// RCU-EMP-002: an employee applies internally. Email comes from the sign-in token when it carries one.
/// Grade and joining date are declared by the employee and checked against the posting's band and tenure
/// rule; HR-TA verifies them at screening.
/// </summary>
public sealed record ApplyInternallyCommand(
    string ReqId,
    string? Email,
    string? Phone,
    string CurrentGrade,
    DateOnly JoinedOn,
    decimal? ExperienceYears,
    bool PrivacyConsent) : ICommand<MyApplicationDto>;

internal sealed class ApplyInternallyCommandValidator : AbstractValidator<ApplyInternallyCommand>
{
    public ApplyInternallyCommandValidator()
    {
        RuleFor(c => c.ReqId).NotEmpty().MaximumLength(EmployeeLimits.ReqIdLength);
        RuleFor(c => c.Email).NotEmpty().WithMessage("Your work email is needed to apply.")
            .EmailAddress().MaximumLength(EmployeeLimits.EmailLength);
        RuleFor(c => c.Phone).MaximumLength(EmployeeLimits.PhoneLength).Matches(@"^[+0-9 ()\-]*$");
        RuleFor(c => c.CurrentGrade).NotEmpty().MaximumLength(EmployeeLimits.GradeLength);
        RuleFor(c => c.JoinedOn).NotEmpty();
        RuleFor(c => c.ExperienceYears).InclusiveBetween(0, 60);
        RuleFor(c => c.PrivacyConsent).Equal(true).WithErrorCode("consent_required")
            .WithMessage("Data-privacy consent is mandatory (FRD §14).");
    }
}

internal sealed class ApplyInternallyCommandHandler(
    IIjpPostingRepository postings,
    IIntakeRepository intake,
    IntakeSaga saga,
    IOptions<EmployeeOptions> options,
    ICurrentUser caller,
    TimeProvider clock) : ICommandHandler<ApplyInternallyCommand, MyApplicationDto>
{
    private const string Source = "IJP";

    public async Task<Result<MyApplicationDto>> Handle(ApplyInternallyCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var posting = await postings.GetAsync(command.ReqId, ct);
        if (posting is null)
        {
            return IjpErrors.NotFound(command.ReqId);
        }

        if (posting.Withdrawn)
        {
            return IjpErrors.Withdrawn(command.ReqId);
        }

        if (!posting.IsListedAt(now))
        {
            return IjpErrors.WindowClosed(command.ReqId);
        }

        var eligible = posting.CheckEligibility(command.CurrentGrade, command.JoinedOn, DateOnly.FromDateTime(now.UtcDateTime));
        if (eligible.IsFailure)
        {
            return eligible.Error!;
        }

        var employeeId = caller.UserId!;
        if ((await intake.ListApplicationsAsync(employeeId, command.ReqId, ct)).Any(a => a.IsActive))
        {
            return IjpErrors.AlreadyApplied(command.ReqId);
        }

        var name = caller.Name ?? employeeId;
        var application = InternalApplication.Start(employeeId, name, posting, command.CurrentGrade, command.JoinedOn, now);
        intake.Add(application);

        var candidate = new NewCandidate(
            name,
            command.Email!.Trim(),
            command.Phone?.Trim(),
            command.ExperienceYears ?? 0,
            Source,
            ReferrerId: null,
            Channel: "employee-portal",
            [new ConsentGiven(ConsentTypes.DataPrivacy, options.Value.ConsentTextVersion, now)],
            ConsentSource: "employee-portal");
        var note = $"IJP · abridged process (PPL-008) · declared grade {application.DeclaredGrade}, joined {command.JoinedOn:yyyy-MM-dd}";
        var result = await saga.RunAsync(application, candidate, note, onCandidate: null, ct);
        return result.IsSuccess ? MyData.From(application) : result.Error!;
    }
}

/// <summary>FRD §9.2.1: the internal window runs <c>n</c> working days from the moment sourcing was unlocked.</summary>
public static class IjpWindow
{
    public static async Task<DateTimeOffset> EndAsync(IWorkingDayCalendar calendar, DateTimeOffset unlockedAt, int workingDays, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (workingDays <= 0)
        {
            return unlockedAt;
        }

        var utc = unlockedAt.ToUniversalTime();
        var lastDay = await calendar.AddAsync(DateOnly.FromDateTime(utc.UtcDateTime), workingDays, ct);
        return new DateTimeOffset(lastDay.ToDateTime(TimeOnly.FromTimeSpan(utc.TimeOfDay)), TimeSpan.Zero);
    }
}
