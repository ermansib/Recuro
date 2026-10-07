using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Candidate.Application.Abstractions;
using Recuro.Candidate.Domain.Candidates;
using CandidateEntity = Recuro.Candidate.Domain.Candidates.Candidate;

namespace Recuro.Candidate.Application.Candidates.Commands.CreateCandidate;

internal sealed class CreateCandidateCommandHandler(
    ICandidateRepository candidates,
    IContactFingerprinter fingerprinter,
    IVendorDirectory vendors,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreateCandidateCommand, CandidateDto>
{
    public async Task<Result<CandidateDto>> Handle(CreateCandidateCommand command, CancellationToken ct)
    {
        if (!CandidateSourceNames.TryParse(command.Source, out var source))
        {
            // The validator rejects unknown sources first; this keeps the handler safe on its own.
            return Error.Validation([new FieldError("source", "invalid_source", "Unknown candidate source.")]);
        }

        if (command.ConsultantId is { Length: > 0 } consultantId && !await vendors.IsActiveConsultantAsync(consultantId, ct))
        {
            return CandidateErrors.ConsultantNotActive;
        }

        // RCU-CND-002: one person = one record. The caller gets the existing id and can use it instead.
        var fingerprints = fingerprinter.Compute(command.Email, command.Phone);
        var duplicate = await candidates.FindByFingerprintAsync(fingerprints, ct);
        if (duplicate is not null)
        {
            return CandidateErrors.Duplicate(duplicate.Id);
        }

        var now = clock.GetUtcNow();
        var consents = command.Consents
            .Select(c => new Consent(Enum.Parse<ConsentType>(c.Type), c.TextVersion.Trim(), c.At ?? now, command.ConsentSource))
            .ToList();
        var details = new CandidateDetails(
            command.Name,
            command.Email,
            command.Phone,
            command.ExperienceYears,
            command.Summary ?? $"{command.ExperienceYears:0.#} yrs",
            command.CurrentCtc,
            command.ExpectedCtc,
            command.NoticeDays);
        var attribution = new SourceAttribution(source, Trim(command.SourceRef), Trim(command.ReferrerId), Trim(command.ConsultantId), Trim(command.Channel));

        var created = CandidateEntity.Create(details, attribution, consents, fingerprints, now);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        candidates.Add(created.Value);
        await unitOfWork.SaveChangesAsync(ct);
        return CandidateDto.From(created.Value);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
