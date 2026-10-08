using FluentValidation;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Careers.Application.Abstractions;
using Recuro.Careers.Domain.Postings;

namespace Recuro.Careers.Application.Postings;

/// <summary>The editable fields of a posting, as HR-TA sends them.</summary>
public sealed record PostingContentInput(
    string Title,
    string Location,
    string LocationFilter,
    string Experience,
    string Qualification,
    string? Industry,
    IReadOnlyList<PostingTagDto> Tags)
{
    public PostingContent ToContent() =>
        new(Title, Location, LocationFilter, Experience, Qualification, Industry, Tags.Select(t => new PostingTag(t.Text.Trim(), t.Tone)).ToList());
}

internal sealed class PostingContentInputValidator : AbstractValidator<PostingContentInput>
{
    public PostingContentInputValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(CareersLimits.TitleLength);
        RuleFor(c => c.Location).NotEmpty().MaximumLength(CareersLimits.LocationLength);
        RuleFor(c => c.LocationFilter).NotEmpty().MaximumLength(CareersLimits.LocationLength);
        RuleFor(c => c.Experience).NotEmpty().MaximumLength(CareersLimits.ShortTextLength);
        RuleFor(c => c.Qualification).NotEmpty().MaximumLength(CareersLimits.ShortTextLength);
        RuleFor(c => c.Industry).MaximumLength(CareersLimits.ShortTextLength);
        RuleFor(c => c.Tags).NotNull().Must(t => t.Count <= CareersLimits.MaxTags)
            .WithMessage($"At most {CareersLimits.MaxTags} tags.");
        RuleForEach(c => c.Tags).ChildRules(tag =>
        {
            tag.RuleFor(t => t.Text).NotEmpty().MaximumLength(CareersLimits.TagTextLength);
            tag.RuleFor(t => t.Tone).Must(ChipTones.All.Contains)
                .WithMessage($"Tone must be one of: {string.Join(", ", ChipTones.All)}.");
        });
    }
}

/// <summary>RCU-CAR-007: HR-TA writes or edits the advert for a requisition. A new one starts as a draft.</summary>
public sealed record UpsertPostingCommand(string ReqId, PostingContentInput Content) : ICommand<PostingAdminDto>;

internal sealed class UpsertPostingCommandValidator : AbstractValidator<UpsertPostingCommand>
{
    public UpsertPostingCommandValidator()
    {
        RuleFor(c => c.ReqId).NotEmpty().MaximumLength(CareersLimits.ReqIdLength);
        RuleFor(c => c.Content).NotNull().SetValidator(new PostingContentInputValidator());
    }
}

internal sealed class UpsertPostingCommandHandler(
    IPostingRepository postings,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UpsertPostingCommand, PostingAdminDto>
{
    public async Task<Result<PostingAdminDto>> Handle(UpsertPostingCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var actor = Actors.From(caller);
        var posting = await postings.GetByReqIdAsync(command.ReqId, ct);
        if (posting is null)
        {
            posting = JobPosting.Draft(command.ReqId, command.Content.ToContent(), actor, now);
            postings.Add(posting);
        }
        else
        {
            var edited = posting.Edit(command.Content.ToContent(), actor, now);
            if (edited.IsFailure)
            {
                return edited.Error!;
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return PostingAdminDto.From(posting);
    }
}

/// <summary>
/// RCU-CAR-007 / RCU-EMP-001: publish once the requisition is approved. While the internal IJP window
/// runs, the public sees it only after the window, unless the HR Head opens it early with a reason.
/// </summary>
public sealed record PublishPostingCommand(string ReqId, bool OpenBeforeIjpWindowEnds, string? Justification) : ICommand<PostingAdminDto>;

internal sealed class PublishPostingCommandValidator : AbstractValidator<PublishPostingCommand>
{
    public PublishPostingCommandValidator()
    {
        RuleFor(c => c.ReqId).NotEmpty().MaximumLength(CareersLimits.ReqIdLength);
        RuleFor(c => c.Justification).MaximumLength(CareersLimits.ReasonLength);
    }
}

internal sealed class PublishPostingCommandHandler(
    IPostingRepository postings,
    ISourcingGateRepository gates,
    IWorkingDayCalendar calendar,
    IOptions<CareersOptions> options,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<PublishPostingCommand, PostingAdminDto>
{
    public async Task<Result<PostingAdminDto>> Handle(PublishPostingCommand command, CancellationToken ct)
    {
        var posting = await postings.GetByReqIdAsync(command.ReqId, ct);
        if (posting is null)
        {
            return PostingErrors.NotFound(command.ReqId);
        }

        if (command.OpenBeforeIjpWindowEnds && !caller.IsInRole(CareersRoles.HrHead))
        {
            return PostingErrors.EarlyReleaseNotAllowed;
        }

        var gate = await gates.GetAsync(command.ReqId, ct);
        var now = clock.GetUtcNow();
        var windowEnd = gate is { IsOpen: true, UnlockedAt: { } unlockedAt }
            ? await IjpWindow.EndAsync(calendar, unlockedAt, options.Value.IjpWindowWorkingDays, ct)
            : now;
        var early = command.OpenBeforeIjpWindowEnds ? new EarlyRelease(command.Justification ?? string.Empty) : null;

        var published = posting.Publish(gate is { IsOpen: true }, windowEnd, early, Actors.From(caller), now);
        if (published.IsFailure)
        {
            return published.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return PostingAdminDto.From(posting);
    }
}

/// <summary>RCU-CAR-007: HR takes a posting down, with a reason that is logged.</summary>
public sealed record UnpublishPostingCommand(string ReqId, string Reason) : ICommand<PostingAdminDto>;

internal sealed class UnpublishPostingCommandValidator : AbstractValidator<UnpublishPostingCommand>
{
    public UnpublishPostingCommandValidator()
    {
        RuleFor(c => c.ReqId).NotEmpty().MaximumLength(CareersLimits.ReqIdLength);
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(CareersLimits.ReasonLength);
    }
}

internal sealed class UnpublishPostingCommandHandler(
    IPostingRepository postings,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UnpublishPostingCommand, PostingAdminDto>
{
    public async Task<Result<PostingAdminDto>> Handle(UnpublishPostingCommand command, CancellationToken ct)
    {
        var posting = await postings.GetByReqIdAsync(command.ReqId, ct);
        if (posting is null)
        {
            return PostingErrors.NotFound(command.ReqId);
        }

        var result = posting.Unpublish(command.Reason, Actors.From(caller), clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return PostingAdminDto.From(posting);
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

/// <summary>Role keys the use cases check, the same strings as the realm roles.</summary>
public static class CareersRoles
{
    public const string HrTa = "hrta";
    public const string HrHead = "hrhead";
}
