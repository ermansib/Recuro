using System.Text.Json.Serialization;
using Recuro.Bgv.Domain.Cases;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;

namespace Recuro.Bgv.Application.Cases;

/// <summary>The frontend's <c>BgvCheck</c>, field for field. <c>sensitiveNote</c> is left out for roles the masking map hides it from.</summary>
public sealed record BgvCheckDto(
    string Type,
    string Label,
    string Detail,
    string Status,
    string Note,
    DateOnly? Date,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SensitiveNote);

/// <summary>The frontend's <c>BgvCase.adverse</c>.</summary>
public sealed record AdverseDto(string Check, string Description, string Action);

/// <summary>
/// The frontend's <c>BgvCase</c> (frontend/src/domain/types.ts), field for field, plus server-side
/// extras the tracker can ignore: <c>status</c>, <c>resolution</c> and <c>needsReassignment</c>.
/// </summary>
public sealed record BgvCaseDto(
    string Id,
    string AppId,
    string Vendor,
    string VendorCaseRef,
    DateTimeOffset? ConsentAt,
    DateOnly InitiatedAt,
    int TatDay,
    int TatTotal,
    IReadOnlyList<BgvCheckDto> Checks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AdverseDto? Adverse,
    string Status,
    string VendorId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ResolutionDto? Resolution,
    bool NeedsReassignment)
{
    public static BgvCaseDto From(BgvCase bgvCase, DateTimeOffset now, bool showSensitive)
    {
        ArgumentNullException.ThrowIfNull(bgvCase);
        return new BgvCaseDto(
            bgvCase.Id.ToString(),
            bgvCase.AppId,
            bgvCase.VendorName,
            bgvCase.VendorCaseRef,
            bgvCase.Consent.At,
            DateOnly.FromDateTime(bgvCase.InitiatedAt.UtcDateTime),
            bgvCase.TatDay(now),
            bgvCase.TatWorkingDays,
            bgvCase.Checks
                .Select(c => new BgvCheckDto(c.Type, c.Label, c.Detail, c.Status.ToString(), c.Note, c.Date, showSensitive ? c.SensitiveNote : null))
                .ToList(),
            bgvCase.Status == CaseStatus.UnderReview && bgvCase.Adverse is { } a ? new AdverseDto(a.Check, a.Description, a.Action.ToString()) : null,
            bgvCase.Status.ToString(),
            bgvCase.VendorId,
            bgvCase.Resolution is { } r ? new ResolutionDto(r.Outcome.ToString(), r.Reason, r.DecidedBy, r.DecidedAt) : null,
            bgvCase.NeedsReassignment);
    }
}

/// <summary>RCU-BGV-006: the documented final decision on an adverse finding.</summary>
public sealed record ResolutionDto(string Outcome, string Reason, string DecidedBy, DateTimeOffset DecidedAt);

/// <summary>One check still blocking the release.</summary>
public sealed record GateBlockerDto(string Type, string Label, string Status);

/// <summary>RCU-BGV-007: <c>{ cleared, blockers }</c> for the Offer and Pipeline services.</summary>
public sealed record ReleaseGateDto(string AppId, string CaseId, bool Cleared, string Status, IReadOnlyList<GateBlockerDto> Blockers)
{
    public static ReleaseGateDto From(BgvCase bgvCase)
    {
        ArgumentNullException.ThrowIfNull(bgvCase);
        return new ReleaseGateDto(
            bgvCase.AppId,
            bgvCase.Id.ToString(),
            bgvCase.Status == CaseStatus.Cleared,
            bgvCase.Status.ToString(),
            bgvCase.ReleaseBlockers().Select(b => new GateBlockerDto(b.Type, b.Label, b.Status.ToString())).ToList());
    }
}

/// <summary>Field sizes shared by validators and the EF configuration.</summary>
public static class BgvLimits
{
    public const int AppIdLength = 40;
    public const int ReqIdLength = 40;
    public const int CandidateIdLength = 64;
    public const int VendorIdLength = 64;
    public const int VendorNameLength = 200;
    public const int VendorCaseRefLength = 100;
    public const int CheckTypeLength = 40;
    public const int LabelLength = 200;
    public const int DetailLength = 500;
    public const int NoteLength = 1000;
    public const int SensitiveNoteLength = 500;
    public const int DescriptionLength = 2000;
    public const int ReasonLength = 2000;
    public const int ActorLength = 200;
    public const int ConsentTextVersionLength = 40;
    public const int ConsentSourceLength = 60;
    public const int SourceLength = 20;
    public const int VersionIdLength = 100;
}

/// <summary>The actor recorded for the current caller or an event's sender.</summary>
internal static class Actors
{
    public static CaseActor From(ICurrentUser user) =>
        new(user.UserId, user.Name ?? user.UserId ?? "unknown", user.Roles.FirstOrDefault());

    public static CaseActor FromEvent(EventMetadata metadata) =>
        new(metadata.ActorId, metadata.ActorName ?? metadata.Source, metadata.ActorRole);
}
