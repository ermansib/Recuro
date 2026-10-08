using FluentValidation;
using Recuro.Bgv.Application.Abstractions;
using Recuro.Bgv.Domain.Cases;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.Bgv.Application.Cases.Commands;

/// <summary>
/// RCU-BGV-003 (manual path): HR-TA records a vendor status update on one check, following §6.3. Vendor
/// webhooks (BGV-004, P1) will call the same domain method.
/// </summary>
public sealed record UpdateCheckCommand(string CaseRef, string CheckType, string Status, string? Note, string? SensitiveNote) : ICommand<BgvCaseDto>;

internal sealed class UpdateCheckCommandValidator : AbstractValidator<UpdateCheckCommand>
{
    public UpdateCheckCommandValidator()
    {
        RuleFor(c => c.Status).IsEnumName(typeof(CheckStatus), caseSensitive: true)
            .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<CheckStatus>())}.");
        RuleFor(c => c.Note).MaximumLength(BgvLimits.NoteLength);
        RuleFor(c => c.SensitiveNote).MaximumLength(BgvLimits.SensitiveNoteLength);
    }
}

internal sealed class UpdateCheckCommandHandler(
    IBgvCaseRepository cases,
    ISensitiveNotePolicy sensitiveNotes,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UpdateCheckCommand, BgvCaseDto>
{
    public async Task<Result<BgvCaseDto>> Handle(UpdateCheckCommand command, CancellationToken ct)
    {
        var bgvCase = await cases.FindAsync(command.CaseRef, ct);
        if (bgvCase is null)
        {
            return BgvErrors.NotFound(command.CaseRef);
        }

        var now = clock.GetUtcNow();
        var updated = bgvCase.UpdateCheck(command.CheckType, Enum.Parse<CheckStatus>(command.Status), command.Note, command.SensitiveNote, Actors.From(caller), now);
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return BgvCaseDto.From(bgvCase, now, await sensitiveNotes.CallerMaySeeAsync(ct));
    }
}
