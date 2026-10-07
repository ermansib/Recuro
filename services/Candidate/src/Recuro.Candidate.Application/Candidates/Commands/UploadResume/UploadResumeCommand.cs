using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Candidate.Application.Abstractions;
using Recuro.Candidate.Domain.Candidates;

namespace Recuro.Candidate.Application.Candidates.Commands.UploadResume;

/// <summary>Stores or replaces the candidate's CV (PDF or Word, up to 5 MB).</summary>
public sealed record UploadResumeCommand(Guid CandidateId, string FileName, string ContentType, long SizeBytes, Stream Content)
    : ICommand<ResumeDto>;

internal sealed class UploadResumeCommandValidator : AbstractValidator<UploadResumeCommand>
{
    public UploadResumeCommandValidator()
    {
        RuleFor(c => c.FileName).NotEmpty().MaximumLength(CandidateLimits.FileNameLength);
        RuleFor(c => c.ContentType).Must(CandidateLimits.ResumeContentTypes.Contains)
            .WithMessage("Upload a PDF or Word document.");
        RuleFor(c => c.SizeBytes).InclusiveBetween(1, CandidateLimits.MaxResumeBytes)
            .WithMessage("The CV must be between 1 byte and 5 MB.");
    }
}

internal sealed class UploadResumeCommandHandler(
    ICandidateRepository candidates,
    IResumeStore store,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UploadResumeCommand, ResumeDto>
{
    public async Task<Result<ResumeDto>> Handle(UploadResumeCommand command, CancellationToken ct)
    {
        var candidate = await candidates.GetAsync(command.CandidateId, ct);
        if (candidate is null)
        {
            return CandidateErrors.NotFound(command.CandidateId);
        }

        var key = $"{candidate.TenantId:N}/{candidate.Id:N}/{Guid.CreateVersion7():N}";
        var resume = new ResumeFile(Path.GetFileName(command.FileName), command.ContentType, command.SizeBytes, key, clock.GetUtcNow());
        var attached = candidate.AttachResume(resume);
        if (attached.IsFailure)
        {
            return attached.Error!;
        }

        await store.SaveAsync(key, command.Content, ct);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await store.DeleteAsync(key, CancellationToken.None);
            throw;
        }

        if (attached.Value is { } previous)
        {
            await store.DeleteAsync(previous, ct);
        }

        return ResumeDto.From(resume);
    }
}
