using System.Globalization;
using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Requisition.Application.Abstractions;
using Recuro.Requisition.Domain.JobDescriptions;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Application.JobDescriptions;

/// <summary>The frontend's <c>JobDescription</c>, field for field.</summary>
public sealed record JobDescriptionDto(
    string Id,
    string ReqId,
    int Version,
    JobDescriptionStatus Status,
    string Purpose,
    IReadOnlyList<string> Responsibilities,
    string ReportsTo,
    string TeamSize,
    string Location,
    string MinQualification,
    string Experience,
    string Grade,
    IReadOnlyList<string> Competencies,
    IReadOnlyList<string> Assessments,
    string Benchmark,
    IReadOnlyList<JobDescriptionHistoryDto> History)
{
    public static JobDescriptionDto From(JobDescription jd)
    {
        ArgumentNullException.ThrowIfNull(jd);
        var c = jd.Content;
        return new JobDescriptionDto(
            jd.Id.ToString(),
            jd.ReqId,
            jd.VersionNumber,
            jd.Status,
            c.Purpose,
            c.Responsibilities,
            c.ReportsTo,
            c.TeamSize,
            c.Location,
            c.MinQualification,
            c.Experience,
            c.Grade,
            c.Competencies,
            c.Assessments,
            c.Benchmark,
            jd.History.Select(h => new JobDescriptionHistoryDto(h.Version, h.Note, h.By, h.At.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).ToList());
    }
}

public sealed record JobDescriptionHistoryDto(int Version, string Note, string By, string At);

/// <summary>The editable part of a JD as the builder sends it. Id, version, status and history are server-owned.</summary>
public sealed record JobDescriptionInput(
    string? Purpose,
    IReadOnlyList<string>? Responsibilities,
    string? ReportsTo,
    string? TeamSize,
    string? Location,
    string? MinQualification,
    string? Experience,
    string? Grade,
    IReadOnlyList<string>? Competencies,
    IReadOnlyList<string>? Assessments,
    string? Benchmark)
{
    public JobDescriptionContent ToContent() => new(
        Purpose?.Trim() ?? string.Empty,
        Responsibilities ?? [],
        ReportsTo?.Trim() ?? string.Empty,
        TeamSize?.Trim() ?? string.Empty,
        Location?.Trim() ?? string.Empty,
        MinQualification?.Trim() ?? string.Empty,
        Experience?.Trim() ?? string.Empty,
        Grade?.Trim() ?? string.Empty,
        Competencies ?? [],
        Assessments ?? [],
        Benchmark?.Trim() ?? string.Empty);
}

public static class JobDescriptionErrors
{
    public static Error NotFound(string key) => Error.NotFound("job_description_not_found", $"No job description for {key}.");
}

/// <summary>S-05: the JD of a requisition (frontend <c>getJobDescription</c>).</summary>
public sealed record GetJobDescriptionQuery(string ReqId) : IQuery<JobDescriptionDto>;

internal sealed class GetJobDescriptionQueryHandler(IJobDescriptionRepository jobDescriptions) : IQueryHandler<GetJobDescriptionQuery, JobDescriptionDto>
{
    public async Task<Result<JobDescriptionDto>> Handle(GetJobDescriptionQuery query, CancellationToken ct)
    {
        var jd = await jobDescriptions.GetByReqIdAsync(query.ReqId, ct);
        return jd is null ? JobDescriptionErrors.NotFound(query.ReqId) : JobDescriptionDto.From(jd);
    }
}

/// <summary>RCU-JD-001..004: submit freezes a new immutable version (frontend <c>submitJobDescription</c>).</summary>
public sealed record SubmitJobDescriptionCommand(Guid Id, JobDescriptionInput Input) : ICommand<JobDescriptionDto>;

internal sealed class SubmitJobDescriptionCommandValidator : AbstractValidator<SubmitJobDescriptionCommand>
{
    public const int TextLength = 2000;
    public const int ItemLength = 500;
    public const int MaxItems = 30;

    public SubmitJobDescriptionCommandValidator()
    {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.Purpose).MaximumLength(TextLength);
        RuleFor(c => c.Input.ReportsTo).MaximumLength(TextLength);
        RuleFor(c => c.Input.TeamSize).MaximumLength(TextLength);
        RuleFor(c => c.Input.Location).MaximumLength(TextLength);
        RuleFor(c => c.Input.MinQualification).MaximumLength(TextLength);
        RuleFor(c => c.Input.Experience).MaximumLength(TextLength);
        RuleFor(c => c.Input.Grade).MaximumLength(TextLength);
        RuleFor(c => c.Input.Benchmark).MaximumLength(TextLength);
        RuleFor(c => c.Input.Responsibilities).Must(l => l is null || l.Count <= MaxItems);
        RuleFor(c => c.Input.Competencies).Must(l => l is null || l.Count <= MaxItems);
        RuleFor(c => c.Input.Assessments).Must(l => l is null || l.Count <= MaxItems);
        RuleForEach(c => c.Input.Responsibilities).MaximumLength(ItemLength);
        RuleForEach(c => c.Input.Competencies).MaximumLength(ItemLength);
        RuleForEach(c => c.Input.Assessments).MaximumLength(ItemLength);
    }
}

internal sealed class SubmitJobDescriptionCommandHandler(
    IJobDescriptionRepository jobDescriptions,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<SubmitJobDescriptionCommand, JobDescriptionDto>
{
    public async Task<Result<JobDescriptionDto>> Handle(SubmitJobDescriptionCommand command, CancellationToken ct)
    {
        var jd = await jobDescriptions.GetByIdAsync(command.Id, ct);
        if (jd is null)
        {
            return JobDescriptionErrors.NotFound(command.Id.ToString());
        }

        var result = jd.Submit(command.Input.ToContent(), user.Name ?? user.UserId ?? "unknown", DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return JobDescriptionDto.From(jd);
    }
}

/// <summary>"Initial draft from MRF": every submitted requisition gets a JD draft seeded from its own fields.</summary>
internal sealed class CreateJobDescriptionOnSubmit(
    IRequisitionRepository requisitions,
    IJobDescriptionRepository jobDescriptions,
    ICurrentUser user,
    TimeProvider clock) : IDomainEventHandler<RequisitionSubmitted>
{
    public async Task Handle(RequisitionSubmitted domainEvent, CancellationToken ct)
    {
        if (await jobDescriptions.ExistsForRequisitionAsync(domainEvent.RequisitionId, ct))
        {
            return;
        }

        var requisition = await requisitions.GetByIdAsync(domainEvent.RequisitionId, ct)
            ?? throw new InvalidOperationException($"Requisition {domainEvent.ReqId} disappeared during submit.");
        var d = requisition.Details;
        var content = new JobDescriptionContent(
            Purpose: string.Empty,
            Responsibilities: [],
            ReportsTo: d.ReportingManager,
            TeamSize: string.Empty,
            Location: d.Location,
            MinQualification: d.Qualifications,
            Experience: string.Empty,
            Grade: d.Grade,
            Competencies: [],
            Assessments: [],
            Benchmark: string.Empty);
        jobDescriptions.Add(JobDescription.CreateFromRequisition(
            requisition.Id,
            requisition.ReqId,
            content,
            user.Name ?? requisition.OwnerName,
            DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)));
    }
}
