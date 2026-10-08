using FluentValidation;
using Recuro.Bgv.Application.Abstractions;
using Recuro.Bgv.Domain.Cases;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.Bgv.Application.Cases.Commands;

/// <summary>The consent evidence sent with an initiation.</summary>
public sealed record ConsentInput(DateTimeOffset? At, string? TextVersion, string? Source);

/// <summary>
/// RCU-BGV-001/002: open a case for an application at the BGV stage. Needs the candidate's consent on file
/// and an active, empanelled BGV agency; the checks come from the Config matrix for the role's grade and
/// risk flags. <paramref name="Scope"/> defaults to delta-only for internal (IJP) candidates.
/// </summary>
public sealed record InitiateCaseCommand(
    string AppId,
    string VendorId,
    string? VendorCaseRef,
    ConsentInput? Consent,
    string Grade,
    IReadOnlyList<string> RoleFlags,
    string? Scope) : ICommand<BgvCaseDto>;

internal sealed class InitiateCaseCommandValidator : AbstractValidator<InitiateCaseCommand>
{
    public InitiateCaseCommandValidator()
    {
        RuleFor(c => c.AppId).NotEmpty().MaximumLength(BgvLimits.AppIdLength);
        RuleFor(c => c.VendorId).NotEmpty().MaximumLength(BgvLimits.VendorIdLength);
        RuleFor(c => c.VendorCaseRef).MaximumLength(BgvLimits.VendorCaseRefLength);
        RuleFor(c => c.Grade).Must(Grades.All.Contains).WithMessage($"Grade must be one of: {string.Join(", ", Grades.All)}.");
        RuleForEach(c => c.RoleFlags).Must(RoleFlags.All.Contains)
            .WithMessage($"Role flags must be among: {string.Join(", ", RoleFlags.All)}.");
        RuleFor(c => c.Scope).IsEnumName(typeof(CheckScope), caseSensitive: true).When(c => c.Scope is not null);
        When(c => c.Consent is not null, () =>
        {
            RuleFor(c => c.Consent!.At).NotNull().OverridePropertyName("consent.at");
            RuleFor(c => c.Consent!.TextVersion).NotEmpty().MaximumLength(BgvLimits.ConsentTextVersionLength).OverridePropertyName("consent.textVersion");
            RuleFor(c => c.Consent!.Source).MaximumLength(BgvLimits.ConsentSourceLength).OverridePropertyName("consent.source");
        });
    }
}

internal sealed class InitiateCaseCommandHandler(
    IBgvCaseRepository cases,
    IBgvRequestRepository requests,
    IVendorDirectory vendors,
    IBgvRules rules,
    ISensitiveNotePolicy sensitiveNotes,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<InitiateCaseCommand, BgvCaseDto>
{
    private const string DefaultConsentSource = "hr-logged";

    public async Task<Result<BgvCaseDto>> Handle(InitiateCaseCommand command, CancellationToken ct)
    {
        if (command.Consent?.At is not { } consentAt)
        {
            return BgvErrors.ConsentRequired;
        }

        var request = await requests.GetAsync(command.AppId, ct);
        if (request is null || !request.AtBgv)
        {
            return BgvErrors.NotAtBgvStage(command.AppId);
        }

        if (await cases.ExistsForApplicationAsync(command.AppId, ct))
        {
            return BgvErrors.AlreadyInitiated(command.AppId);
        }

        var vendor = await vendors.GetAsync(command.VendorId, ct);
        if (vendor is null || !vendor.IsActiveBgvAgency)
        {
            return BgvErrors.VendorNotActive(command.VendorId);
        }

        var scope = command.Scope is null
            ? request.IsInternal ? CheckScope.Delta : CheckScope.Full
            : Enum.Parse<CheckScope>(command.Scope);
        var role = new RoleProfile(command.Grade, command.RoleFlags.ToHashSet(StringComparer.Ordinal), scope);
        var matrix = await rules.GetCheckMatrixAsync(ct);
        var tat = await rules.GetTatWorkingDaysAsync(ct);

        var now = clock.GetUtcNow();
        var due = await rules.AddWorkingDaysAsync(DateOnly.FromDateTime(now.UtcDateTime), tat, ct);
        var dueAt = new DateTimeOffset(due.ToDateTime(TimeOnly.FromDateTime(now.UtcDateTime)), TimeSpan.Zero);

        var bgvCase = BgvCase.Initiate(
            request.AppId,
            request.ReqId,
            request.CandidateId,
            new VendorRef(vendor.VendorId, vendor.Name),
            command.VendorCaseRef ?? string.Empty,
            new Consent(consentAt, command.Consent.TextVersion!.Trim(), string.IsNullOrWhiteSpace(command.Consent.Source) ? DefaultConsentSource : command.Consent.Source.Trim()),
            role,
            matrix.ConfigVersionId,
            CheckPlanner.Plan(matrix.Rules, role),
            tat,
            dueAt,
            Actors.From(caller),
            now);
        cases.Add(bgvCase);
        await unitOfWork.SaveChangesAsync(ct);
        return BgvCaseDto.From(bgvCase, now, await sensitiveNotes.CallerMaySeeAsync(ct));
    }
}
