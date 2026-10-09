using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Onboarding.Application.Abstractions;
using Recuro.Onboarding.Domain.Cases;

namespace Recuro.Onboarding.Application.Cases.Commands;

/// <summary>RCU-ONB-003: uploads (or replaces) one §13 document; it waits for verification.</summary>
public sealed record UploadDocumentCommand(string CaseRef, string DocumentType, string FileName, string ContentType, long Length, Stream Content) : ICommand<OnboardingCaseDto>;

internal sealed class UploadDocumentCommandValidator : AbstractValidator<UploadDocumentCommand>
{
    public UploadDocumentCommandValidator()
    {
        RuleFor(c => c.FileName).NotEmpty().MaximumLength(OnboardingLimits.FileNameLength).OverridePropertyName("file");
        RuleFor(c => c.ContentType)
            .Must(OnboardingLimits.DocumentContentTypes.Contains)
            .WithErrorCode("unsupported_type")
            .WithMessage("Upload a PDF, JPEG or PNG.")
            .OverridePropertyName("file");
        RuleFor(c => c.Length)
            .InclusiveBetween(1, OnboardingLimits.MaxDocumentBytes)
            .WithErrorCode("file_size")
            .WithMessage("The file must be between 1 byte and 10 MB.")
            .OverridePropertyName("file");
    }
}

internal sealed class UploadDocumentCommandHandler(
    IOnboardingCaseRepository cases,
    IDocumentStore store,
    CaseViews views,
    ICurrentUser caller,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UploadDocumentCommand, OnboardingCaseDto>
{
    public async Task<Result<OnboardingCaseDto>> Handle(UploadDocumentCommand command, CancellationToken ct)
    {
        var onboardingCase = await cases.FindAsync(command.CaseRef, ct);
        if (onboardingCase is null)
        {
            return OnboardingErrors.NotFound(command.CaseRef);
        }

        if (onboardingCase.IsClosed)
        {
            return OnboardingErrors.Closed(onboardingCase.Status);
        }

        if (!onboardingCase.Documents.Any(d => d.Type == command.DocumentType))
        {
            return OnboardingErrors.DocumentNotFound(command.DocumentType);
        }

        // The file goes to the store first; if the database commit then fails, an orphan file is the
        // worst case, never a document row pointing at nothing.
        var key = $"{tenant.RequiredTenantId:N}/{onboardingCase.Id:N}/{command.DocumentType}-{Guid.CreateVersion7():N}";
        await store.SaveAsync(key, command.Content, ct);

        var file = new StoredFile(Path.GetFileName(command.FileName), command.ContentType, command.Length, key);
        var attached = onboardingCase.AttachDocument(command.DocumentType, file, Actors.From(caller), clock.GetUtcNow());
        if (attached.IsFailure)
        {
            await store.DeleteAsync(key, ct);
            return attached.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        if (attached.Value is { } replaced)
        {
            await store.DeleteAsync(replaced, ct);
        }

        return await views.ToDtoAsync(onboardingCase, ct);
    }
}

/// <summary>RCU-ONB-003: HR Ops verifies or rejects an uploaded document (a rejection needs a note).</summary>
public sealed record ReviewDocumentCommand(string CaseRef, string DocumentType, bool Verified, string? Note) : ICommand<OnboardingCaseDto>;

internal sealed class ReviewDocumentCommandValidator : AbstractValidator<ReviewDocumentCommand>
{
    public ReviewDocumentCommandValidator() => RuleFor(c => c.Note).MaximumLength(OnboardingLimits.NoteLength);
}

internal sealed class ReviewDocumentCommandHandler(
    IOnboardingCaseRepository cases,
    CaseViews views,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ReviewDocumentCommand, OnboardingCaseDto>
{
    public async Task<Result<OnboardingCaseDto>> Handle(ReviewDocumentCommand command, CancellationToken ct)
    {
        var onboardingCase = await cases.FindAsync(command.CaseRef, ct);
        if (onboardingCase is null)
        {
            return OnboardingErrors.NotFound(command.CaseRef);
        }

        var reviewed = onboardingCase.ReviewDocument(command.DocumentType, command.Verified, command.Note, Actors.From(caller), clock.GetUtcNow());
        if (reviewed.IsFailure)
        {
            return reviewed.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return await views.ToDtoAsync(onboardingCase, ct);
    }
}

/// <summary>RCU-ONB-003: marks the employee file complete, or 409 <c>file_incomplete</c> with the missing list.</summary>
public sealed record CompleteFileCommand(string CaseRef) : ICommand<FileStatusDto>;

internal sealed class CompleteFileCommandHandler(
    IOnboardingCaseRepository cases,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CompleteFileCommand, FileStatusDto>
{
    public async Task<Result<FileStatusDto>> Handle(CompleteFileCommand command, CancellationToken ct)
    {
        var onboardingCase = await cases.FindAsync(command.CaseRef, ct);
        if (onboardingCase is null)
        {
            return OnboardingErrors.NotFound(command.CaseRef);
        }

        var completed = onboardingCase.CompleteFile(Actors.From(caller), clock.GetUtcNow());
        if (completed.IsFailure)
        {
            return completed.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return FileStatus.Of(onboardingCase);
    }
}

internal static class FileStatus
{
    public static FileStatusDto Of(OnboardingCase c)
    {
        var missing = c.MissingDocuments();
        return new FileStatusDto(
            c.Id.ToString(),
            c.AppId,
            missing.Count == 0 && c.FileCompletedAt is not null,
            c.FileCompletedAt,
            missing.Select(m => new MissingDocumentDto(m.Type, m.Label, m.Status.ToString())).ToList());
    }
}
