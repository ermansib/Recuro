using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Careers.Domain.Postings;

namespace Recuro.Careers.Application.Postings;

/// <summary>A chip on the card, the frontend's <c>{ text, tone }</c>.</summary>
public sealed record PostingTagDto(string Text, string Tone);

/// <summary>The frontend's public <c>JobPosting</c> (frontend/src/domain/types.ts), field for field. No PII.</summary>
public sealed record JobPostingDto(
    string Id,
    string ReqId,
    string Title,
    string Location,
    string LocationFilter,
    string Experience,
    string Qualification,
    IReadOnlyList<PostingTagDto> Tags)
{
    public static JobPostingDto From(JobPosting posting)
    {
        ArgumentNullException.ThrowIfNull(posting);
        return new JobPostingDto(
            posting.PostingId,
            posting.ReqId,
            posting.Title,
            posting.Location,
            posting.LocationFilter,
            posting.Experience,
            posting.Qualification,
            posting.Tags.Select(t => new PostingTagDto(t.Text, t.Tone)).ToList());
    }
}

/// <summary>RCU-CAR-001: one page of public search results. <c>nextCursor</c> is null on the last page.</summary>
public sealed record JobSearchPageDto(IReadOnlyList<JobPostingDto> Items, string? NextCursor);

/// <summary>One logged change on a posting (HR view).</summary>
public sealed record PostingHistoryDto(string Action, string By, DateTimeOffset At, string? Reason);

/// <summary>HR's view of a posting: the public card plus its publication state and log (RCU-CAR-007).</summary>
public sealed record PostingAdminDto(
    JobPostingDto Posting,
    string? Industry,
    string Status,
    DateTimeOffset? VisibleFrom,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PostingHistoryDto> History)
{
    public static PostingAdminDto From(JobPosting posting)
    {
        ArgumentNullException.ThrowIfNull(posting);
        return new PostingAdminDto(
            JobPostingDto.From(posting),
            posting.Industry,
            posting.Status.ToString(),
            posting.VisibleFrom,
            posting.UpdatedAt,
            posting.History.Select(h => new PostingHistoryDto(h.Action.ToString(), h.By, h.At, h.Reason)).ToList());
    }
}

/// <summary>Field sizes shared by validators and the EF configuration.</summary>
public static class CareersLimits
{
    public const int ReqIdLength = 40;
    public const int PostingIdLength = 48;
    public const int AppIdLength = 40;
    public const int CandidateIdLength = 64;
    public const int TitleLength = 200;
    public const int LocationLength = 120;
    public const int ShortTextLength = 120;
    public const int TagTextLength = 60;
    public const int ToneLength = 20;
    public const int MaxTags = 8;
    public const int ReasonLength = 1000;
    public const int ActorLength = 200;
    public const int QueryLength = 100;
    public const int NameLength = 200;
    public const int EmailLength = 254;
    public const int PhoneLength = 32;
    public const int FileNameLength = 255;
    public const int ConsentTypeLength = 40;
    public const int TextVersionLength = 40;
    public const int CodeLength = 60;
    public const int ClientKeyLength = 64;
}

/// <summary>The tones the frontend's <c>ChipTone</c> allows.</summary>
public static class ChipTones
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "green", "red", "amber", "slate", "navy", "gold", "purple", "teal",
    };
}

internal static class Actors
{
    public static PostingActor From(ICurrentUser user) => new(user.UserId, user.Name ?? user.UserId ?? "unknown");
}
