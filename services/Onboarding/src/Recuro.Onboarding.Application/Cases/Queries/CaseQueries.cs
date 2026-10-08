using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Onboarding.Application.Abstractions;
using Recuro.Onboarding.Domain.Cases;

namespace Recuro.Onboarding.Application.Cases.Queries;

/// <summary>The case for an application (or by case id), as screen S-13 shows it.</summary>
public sealed record GetCaseQuery(string CaseRef) : IQuery<OnboardingCaseDto>;

internal sealed class GetCaseQueryHandler(IOnboardingCaseRepository cases, CaseViews views) : IQueryHandler<GetCaseQuery, OnboardingCaseDto>
{
    public async Task<Result<OnboardingCaseDto>> Handle(GetCaseQuery query, CancellationToken ct)
    {
        var onboardingCase = await cases.FindAsync(query.CaseRef, ct);
        return onboardingCase is null ? OnboardingErrors.NotFound(query.CaseRef) : await views.ToDtoAsync(onboardingCase, ct);
    }
}

/// <summary>Cases by status (default: every running case), soonest joining first.</summary>
public sealed record ListCasesQuery(string? Status) : IQuery<IReadOnlyList<OnboardingCaseDto>>;

internal sealed class ListCasesQueryHandler(IOnboardingCaseRepository cases, CaseViews views) : IQueryHandler<ListCasesQuery, IReadOnlyList<OnboardingCaseDto>>
{
    public async Task<Result<IReadOnlyList<OnboardingCaseDto>>> Handle(ListCasesQuery query, CancellationToken ct)
    {
        CaseStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<CaseStatus>(query.Status, ignoreCase: true, out var parsed) || int.TryParse(query.Status, out _))
            {
                return Error.Validation([new FieldError("status", "status", $"status must be one of: {string.Join(", ", Enum.GetNames<CaseStatus>())}.")]);
            }

            status = parsed;
        }

        return Result.Success(await views.ToDtosAsync(await cases.ListAsync(status, OnboardingLimits.ListLimit, ct), ct));
    }
}

/// <summary>RCU-ONB-003: <c>{ complete, missing[] }</c> without changing anything.</summary>
public sealed record GetFileStatusQuery(string CaseRef) : IQuery<FileStatusDto>;

internal sealed class GetFileStatusQueryHandler(IOnboardingCaseRepository cases) : IQueryHandler<GetFileStatusQuery, FileStatusDto>
{
    public async Task<Result<FileStatusDto>> Handle(GetFileStatusQuery query, CancellationToken ct)
    {
        var onboardingCase = await cases.FindAsync(query.CaseRef, ct);
        return onboardingCase is null ? OnboardingErrors.NotFound(query.CaseRef) : Commands.FileStatus.Of(onboardingCase);
    }
}

/// <summary>The stored file of one §13 document.</summary>
public sealed record GetDocumentFileQuery(string CaseRef, string DocumentType) : IQuery<FileContent>;

internal sealed class GetDocumentFileQueryHandler(IOnboardingCaseRepository cases, IDocumentStore store) : IQueryHandler<GetDocumentFileQuery, FileContent>
{
    public async Task<Result<FileContent>> Handle(GetDocumentFileQuery query, CancellationToken ct)
    {
        var onboardingCase = await cases.FindAsync(query.CaseRef, ct);
        if (onboardingCase is null)
        {
            return OnboardingErrors.NotFound(query.CaseRef);
        }

        var document = onboardingCase.Documents.FirstOrDefault(d => d.Type == query.DocumentType);
        if (document?.StorageKey is null)
        {
            return OnboardingErrors.DocumentNotFound(query.DocumentType);
        }

        var content = await store.OpenAsync(document.StorageKey, ct);
        return content is null
            ? OnboardingErrors.DocumentNotFound(query.DocumentType)
            : new FileContent(content, document.ContentType ?? "application/octet-stream", document.FileName ?? document.Type);
    }
}

/// <summary>RCU-ONB-005: the confirmation letter of a confirmed employee, as a PDF.</summary>
public sealed record GetConfirmationLetterQuery(string CaseRef) : IQuery<FileContent>;

internal sealed class GetConfirmationLetterQueryHandler(
    IOnboardingCaseRepository cases,
    ICandidateDirectory candidates,
    IConfirmationLetterRenderer renderer) : IQueryHandler<GetConfirmationLetterQuery, FileContent>
{
    public async Task<Result<FileContent>> Handle(GetConfirmationLetterQuery query, CancellationToken ct)
    {
        var onboardingCase = await cases.FindAsync(query.CaseRef, ct);
        if (onboardingCase is null)
        {
            return OnboardingErrors.NotFound(query.CaseRef);
        }

        var confirmation = onboardingCase.Decisions.LastOrDefault(d => d.Outcome == ProbationOutcome.Confirm);
        if (onboardingCase.Status != CaseStatus.Confirmed || confirmation is null)
        {
            return Error.NotFound("confirmation_letter_not_found", "The employee has not been confirmed.");
        }

        var name = await candidates.GetNameAsync(onboardingCase.CandidateId, ct) ?? onboardingCase.CandidateId;
        var pdf = renderer.Render(onboardingCase, confirmation, name);
        return new FileContent(new MemoryStream(pdf, writable: false), "application/pdf", $"confirmation-{onboardingCase.AppId}.pdf");
    }
}
