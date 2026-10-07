using System.Text.Json;
using System.Text.Json.Serialization;
using Recuro.Pipeline.Domain.Applications;
using ApplicationEntity = Recuro.Pipeline.Domain.Applications.Application;

namespace Recuro.Pipeline.Application.Applications;

/// <summary>One line of the frontend's <c>Application.stageHistory</c>. <c>from</c> is null for the first line.</summary>
public sealed record StageMoveDto(string? From, string To, string By, DateTimeOffset At);

/// <summary>The frontend's <c>Application.rejection</c>; dates are <c>YYYY-MM-DD</c>.</summary>
public sealed record RejectionDto(string Reason, DateOnly RegretDueBy, DateOnly RetainUntil);

/// <summary>The frontend's <c>Application</c> (frontend/src/domain/types.ts), field for field.</summary>
public sealed record ApplicationDto(
    string AppId,
    string ReqId,
    string CandidateId,
    string Stage,
    IReadOnlyList<StageMoveDto> StageHistory,
    string Note,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RejectionDto? Rejection)
{
    public static ApplicationDto From(ApplicationEntity application)
    {
        ArgumentNullException.ThrowIfNull(application);
        return new ApplicationDto(
            application.AppId,
            application.ReqId,
            application.CandidateId,
            application.Stage.ToString(),
            application.StageHistory.Select(m => new StageMoveDto(m.From?.ToString(), m.To.ToString(), m.By, m.At)).ToList(),
            application.Note,
            application.Rejection is { } r ? new RejectionDto(r.Reason, r.RegretDueBy, r.RetainUntil) : null);
    }
}

/// <summary>The frontend's <c>PipelineCard</c>: the application plus its candidate as the Candidate service returned it (masked for the caller).</summary>
public sealed record PipelineCardDto(ApplicationDto Application, JsonElement Candidate);

/// <summary>One kanban column (RCU-PPL-006).</summary>
public sealed record BoardColumnDto(string Stage, int Count, IReadOnlyList<PipelineCardDto> Cards);

/// <summary>
/// RCU-PPL-006: the whole board in one payload: the six columns with their cards and counts, plus counts
/// of applications off the board (Hold, Rejected, Withdrawn, PreBoarding and later).
/// </summary>
public sealed record PipelineBoardDto(string ReqId, int Total, IReadOnlyList<BoardColumnDto> Columns, IReadOnlyDictionary<string, int> OffBoard);

/// <summary>Candidate sources in the frontend's spelling (<c>CandidateSource</c>).</summary>
public static class ApplicationSources
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "IJP", "Referral", "Portal", "Campus", "Walk-in", "Consultant", "Social", "LinkedIn", "Direct",
    };
}

/// <summary>Field sizes shared by validators and the EF configuration.</summary>
public static class PipelineLimits
{
    public const int AppIdLength = 40;
    public const int ReqIdLength = 40;
    public const int CandidateIdLength = 64;
    public const int SourceLength = 20;
    public const int NoteLength = 500;
    public const int ReasonLength = 1000;
    public const int ActorLength = 200;
}

/// <summary>The actor recorded on stage moves made for the current caller.</summary>
internal static class Actors
{
    public static StageActor From(BuildingBlocks.Application.Abstractions.ICurrentUser user) =>
        new(user.UserId, user.Name ?? user.UserId ?? "unknown", user.Roles.FirstOrDefault());

    public static StageActor FromEvent(BuildingBlocks.Application.IntegrationEvents.EventMetadata metadata) =>
        new(metadata.ActorId, metadata.ActorName ?? metadata.Source, metadata.ActorRole);
}
