using FluentValidation;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Employee.Application.Abstractions;
using Recuro.Employee.Application.Ijp;
using Recuro.Employee.Domain.Ijp;
using Recuro.Employee.Domain.Intake;
using Recuro.Employee.Domain.Referrals;

namespace Recuro.Employee.Application.Referrals;

/// <summary>RCU-EMP-005: one of the employee's own referrals, coarse status only.</summary>
public sealed record MyReferralDto(
    string Id,
    string ReqId,
    string CandidateName,
    string Relationship,
    bool BonusEligible,
    string? AppId,
    string Status,
    string Progress,
    DateTimeOffset SubmittedAt);

/// <summary>
/// RCU-EMP-003: an employee refers someone. The §7.1 COI declaration is mandatory (400 without it). The
/// referred person becomes a Referral-sourced candidate and application through the intake saga, with the
/// referrer recorded on both.
/// </summary>
public sealed record SubmitReferralCommand(
    string ReqId,
    string Name,
    string Email,
    string? Phone,
    decimal? ExperienceYears,
    string Relationship,
    bool CoiAccepted,
    string? ReferrerEmail) : ICommand<MyReferralDto>;

internal sealed class SubmitReferralCommandValidator : AbstractValidator<SubmitReferralCommand>
{
    public SubmitReferralCommandValidator()
    {
        RuleFor(c => c.CoiAccepted).Equal(true).WithErrorCode("consent_required")
            .WithMessage("Accept the conflict-of-interest declaration to refer someone (FRD §7.1).");
        RuleFor(c => c.ReqId).NotEmpty().MaximumLength(EmployeeLimits.ReqIdLength);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(EmployeeLimits.NameLength);
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(EmployeeLimits.EmailLength);
        RuleFor(c => c.Phone).MaximumLength(EmployeeLimits.PhoneLength).Matches(@"^[+0-9 ()\-]*$");
        RuleFor(c => c.ExperienceYears).InclusiveBetween(0, 60);
        RuleFor(c => c.Relationship).IsEnumName(typeof(ReferralRelationship), caseSensitive: false)
            .WithMessage($"Relationship must be one of: {string.Join(", ", Enum.GetNames<ReferralRelationship>())}.");
    }
}

internal sealed class SubmitReferralCommandHandler(
    ISourcingGateRepository gates,
    IIntakeRepository intake,
    IntakeSaga saga,
    IOptions<EmployeeOptions> options,
    ICurrentUser caller,
    TimeProvider clock) : ICommandHandler<SubmitReferralCommand, MyReferralDto>
{
    private const string Source = "Referral";

    public async Task<Result<MyReferralDto>> Handle(SubmitReferralCommand command, CancellationToken ct)
    {
        if (command.ReferrerEmail is { Length: > 0 } own && string.Equals(own.Trim(), command.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return ReferralErrors.SelfReferral;
        }

        // RCU-MRF-006: referrals feed sourcing, so the requisition must be open for it.
        var gate = await gates.GetAsync(command.ReqId, ct);
        if (gate is not { IsOpen: true })
        {
            return IjpErrors.SourcingLocked(command.ReqId);
        }

        var now = clock.GetUtcNow();
        var settings = options.Value;
        var referrerId = caller.UserId!;
        var submitted = Referral.Submit(
            referrerId,
            caller.Name ?? referrerId,
            command.ReqId,
            command.Name,
            Enum.Parse<ReferralRelationship>(command.Relationship, ignoreCase: true),
            command.CoiAccepted,
            settings.BonusPolicy,
            now);
        if (submitted.IsFailure)
        {
            return submitted.Error!;
        }

        var referral = submitted.Value;
        intake.Add(referral);

        // The referrer attests the COI declaration; the privacy notice goes to the candidate with HR's first contact.
        var consents = new[]
        {
            new ConsentGiven(ConsentTypes.DataPrivacy, settings.ConsentTextVersion, now),
            new ConsentGiven(ConsentTypes.ConflictOfInterest, settings.ConsentTextVersion, now),
        };
        var candidate = new NewCandidate(
            command.Name.Trim(),
            command.Email.Trim(),
            command.Phone?.Trim(),
            command.ExperienceYears ?? 0,
            Source,
            referrerId,
            Channel: "employee-referral",
            consents,
            ConsentSource: "referrer-attested");
        var note = $"Referral by {referral.EmployeeName} ({referral.Relationship}) · COI declared";
        var result = await saga.RunAsync(
            referral,
            candidate,
            note,
            person =>
            {
                if (!person.CreatedHere)
                {
                    referral.MatchedExisting(person.CandidateId);
                }
            },
            ct);
        return result.IsSuccess ? MyData.From(referral) : result.Error!;
    }
}

/// <summary>RCU-EMP-005: the caller's own IJP applications. Scoped to the token's subject, server-side.</summary>
public sealed record MyApplicationsQuery : IQuery<IReadOnlyList<MyApplicationDto>>;

internal sealed class MyApplicationsQueryHandler(IIntakeRepository intake, ICurrentUser caller)
    : IQueryHandler<MyApplicationsQuery, IReadOnlyList<MyApplicationDto>>
{
    public async Task<Result<IReadOnlyList<MyApplicationDto>>> Handle(MyApplicationsQuery query, CancellationToken ct) =>
        Result.Success<IReadOnlyList<MyApplicationDto>>((await intake.ListApplicationsAsync(caller.UserId!, ct)).Select(MyData.From).ToList());
}

/// <summary>RCU-EMP-005: the caller's own referrals. Scoped to the token's subject, server-side.</summary>
public sealed record MyReferralsQuery : IQuery<IReadOnlyList<MyReferralDto>>;

internal sealed class MyReferralsQueryHandler(IIntakeRepository intake, ICurrentUser caller)
    : IQueryHandler<MyReferralsQuery, IReadOnlyList<MyReferralDto>>
{
    public async Task<Result<IReadOnlyList<MyReferralDto>>> Handle(MyReferralsQuery query, CancellationToken ct) =>
        Result.Success<IReadOnlyList<MyReferralDto>>((await intake.ListReferralsAsync(caller.UserId!, ct)).Select(MyData.From).ToList());
}

/// <summary>Maps records to the employee's coarse view. A failed intake reads as "Not submitted".</summary>
internal static class MyData
{
    public static MyApplicationDto From(InternalApplication a) =>
        new(a.Id.ToString(), a.ReqId, a.PostingTitle, a.AppId, Status(a), a.Progress.ToString(), a.SubmittedAt);

    public static MyReferralDto From(Referral r) =>
        new(r.Id.ToString(), r.ReqId, r.CandidateName, r.Relationship.ToString(), r.BonusEligible, r.AppId, Status(r), r.Progress.ToString(), r.SubmittedAt);

    private static string Status(IntakeRecord record) => record.State switch
    {
        IntakeState.Completed => "Submitted",
        IntakeState.Started or IntakeState.CandidateRecorded => "Processing",
        _ => "NotSubmitted",
    };
}
