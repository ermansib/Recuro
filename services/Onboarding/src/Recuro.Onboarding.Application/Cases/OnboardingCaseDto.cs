using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.Onboarding.Domain.Cases;

namespace Recuro.Onboarding.Application.Cases;

/// <summary>One Annexure E item (frontend <c>OnboardingChecklistItem</c>).</summary>
public sealed record ChecklistItemDto(string Key, string Label, bool Done, string Remarks, DateTimeOffset? UpdatedAt, string? UpdatedBy);

/// <summary>One §13 document (frontend <c>OnboardingDocument</c>). The storage key never leaves the service.</summary>
public sealed record DocumentDto(
    string Type,
    string Label,
    bool Mandatory,
    string Status,
    string? FileName,
    long? Size,
    DateTimeOffset? UploadedAt,
    string? UploadedBy,
    DateTimeOffset? ReviewedAt,
    string? ReviewedBy,
    string Note);

/// <summary>One §9.9/§9.10 milestone (frontend <c>OnboardingMilestone</c>).</summary>
public sealed record MilestoneDto(
    string Id,
    string Kind,
    string Label,
    string Phase,
    int Cycle,
    DateOnly DueOn,
    string Status,
    DateTimeOffset? RaisedAt,
    DateTimeOffset? CompletedAt,
    string? CompletedBy,
    string Notes,
    string? TicketRef);

/// <summary>One recorded probation decision (frontend <c>ProbationDecision</c>).</summary>
public sealed record DecisionDto(int Cycle, string Outcome, string Reason, int? ExtendedByMonths, DateOnly? NewProbationEnd, string DecidedBy, DateTimeOffset DecidedAt);

/// <summary>
/// The onboarding record behind screen S-13 (frontend <c>OnboardingCase</c> in
/// frontend/src/domain/types.ts, FRD data model <c>OnboardingCase</c>). Ids only: names come from the
/// Candidate service, so nothing here needs masking.
/// </summary>
public sealed record OnboardingCaseDto(
    string Id,
    string AppId,
    string? OfferId,
    string ReqId,
    string CandidateId,
    string Status,
    DateOnly JoiningDate,
    int DaysToJoining,
    DateTimeOffset AcceptedAt,
    string BgvStatus,
    string? ReportingManagerId,
    string? ReportingManager,
    string? Buddy,
    int ChecklistPercent,
    DateTimeOffset? Day1ReadyAt,
    IReadOnlyList<ChecklistItemDto> Checklist,
    bool FileComplete,
    DateTimeOffset? FileCompletedAt,
    IReadOnlyList<string> MissingDocuments,
    IReadOnlyList<DocumentDto> Documents,
    int ProbationMonths,
    DateOnly ProbationEndsOn,
    int ProbationCycle,
    IReadOnlyList<MilestoneDto> Milestones,
    IReadOnlyList<DecisionDto> Decisions,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? CancelledAt,
    string RulesVersionId)
{
    public static OnboardingCaseDto From(OnboardingCase c, BgvStatus bgv, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(c);
        var missing = c.MissingDocuments();
        return new OnboardingCaseDto(
            c.Id.ToString(),
            c.AppId,
            c.OfferId,
            c.ReqId,
            c.CandidateId,
            c.Status.ToString(),
            c.JoiningDate,
            c.JoiningDate.DayNumber - today.DayNumber,
            c.AcceptedAt,
            bgv.ToString(),
            c.ReportingManagerId,
            c.ReportingManager,
            c.Buddy,
            c.ChecklistPercent,
            c.Day1ReadyAt,
            c.Checklist.OrderBy(i => i.Position).Select(i => new ChecklistItemDto(i.Key, i.Label, i.Done, i.Remarks, i.UpdatedAt, i.UpdatedBy)).ToList(),
            missing.Count == 0 && c.FileCompletedAt is not null,
            c.FileCompletedAt,
            missing.Select(m => m.Type).ToList(),
            c.Documents.OrderBy(d => d.Position)
                .Select(d => new DocumentDto(d.Type, d.Label, d.Mandatory, d.Status.ToString(), d.FileName, d.Size, d.UploadedAt, d.UploadedBy, d.ReviewedAt, d.ReviewedBy, d.Note))
                .ToList(),
            c.ProbationMonths,
            c.ProbationEndsOn,
            c.ProbationCycle,
            c.Milestones.OrderBy(m => m.DueOn).ThenBy(m => m.Cycle)
                .Select(m => new MilestoneDto(m.Id.ToString(), m.Kind, m.Label, m.Phase.ToString(), m.Cycle, m.DueOn, m.Status.ToString(), m.RaisedAt, m.CompletedAt, m.CompletedBy, m.Notes, m.TicketRef))
                .ToList(),
            c.Decisions.OrderBy(d => d.DecidedAt)
                .Select(d => new DecisionDto(d.Cycle, d.Outcome.ToString(), d.Reason, d.ExtendedByMonths, d.NewProbationEnd, d.DecidedBy, d.DecidedAt))
                .ToList(),
            c.ConfirmedAt,
            c.CancelledAt,
            c.RulesVersionId);
    }
}

/// <summary>RCU-ONB-003: <c>{ complete, missing[] }</c> for the file-complete check.</summary>
public sealed record FileStatusDto(string CaseId, string AppId, bool Complete, DateTimeOffset? CompletedAt, IReadOnlyList<MissingDocumentDto> Missing);

public sealed record MissingDocumentDto(string Type, string Label, string Status);

/// <summary>A stored file on its way to the client.</summary>
public sealed record FileContent(Stream Content, string ContentType, string FileName);

/// <summary>Sizes of stored strings, shared by validators and the EF model.</summary>
public static class OnboardingLimits
{
    public const int AppIdLength = 40;
    public const int ReqIdLength = 40;
    public const int CandidateIdLength = 64;
    public const int OfferIdLength = 64;
    public const int KeyLength = 60;
    public const int LabelLength = 200;
    public const int RemarksLength = 500;
    public const int NoteLength = 1000;
    public const int ReasonLength = 2000;
    public const int ActorLength = 200;
    public const int PersonIdLength = 100;
    public const int VersionIdLength = 64;
    public const int FileNameLength = 255;
    public const int ContentTypeLength = 100;
    public const int StorageKeyLength = 300;
    public const int TicketRefLength = 100;
    public const long MaxDocumentBytes = 10 * 1024 * 1024;
    public const int ListLimit = 200;

    /// <summary>Scans and photos of §13 documents.</summary>
    public static readonly IReadOnlySet<string> DocumentContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
    };
}

internal static class Actors
{
    public static CaseActor From(ICurrentUser user) =>
        new(user.UserId, user.Name ?? user.UserId ?? "unknown", user.Roles.FirstOrDefault());

    public static CaseActor FromEvent(EventMetadata metadata) =>
        new(metadata.ActorId, metadata.ActorName ?? metadata.Source, metadata.ActorRole);
}
