using Recuro.BuildingBlocks.Domain;

namespace Recuro.Interview.Domain.Applications;

/// <summary>
/// This service's copy of an application's stage, kept from <c>pipeline.stage.changed</c>. It answers
/// "may this application be interviewed?" without a call to Pipeline, and carries the requisition and
/// candidate ids every round needs.
/// </summary>
public sealed class ApplicationTrack : ITenantOwned
{
    public const string InterviewStage = "Interview";

    /// <summary>Stages that end the interview process for good: open rounds are cancelled.</summary>
    public static readonly IReadOnlySet<string> ClosedStages = new HashSet<string>(StringComparer.Ordinal) { "Rejected", "Withdrawn" };

    private ApplicationTrack()
    {
    }

    public Guid TenantId { get; private set; }

    public string AppId { get; private set; } = string.Empty;

    public string ReqId { get; private set; } = string.Empty;

    public string CandidateId { get; private set; } = string.Empty;

    public string Stage { get; private set; } = string.Empty;

    /// <summary>When Pipeline made the move. Older events are ignored, so redelivery can't move it back.</summary>
    public DateTimeOffset StageChangedAt { get; private set; }

    public bool InInterview => Stage == InterviewStage;

    public static ApplicationTrack Start(string appId, string reqId, string candidateId, string stage, DateTimeOffset at) => new()
    {
        AppId = appId,
        ReqId = reqId,
        CandidateId = candidateId,
        Stage = stage,
        StageChangedAt = at,
    };

    /// <summary>Applies a stage move. Returns false when the event is older than what is already known.</summary>
    public bool Move(string stage, DateTimeOffset at)
    {
        if (at < StageChangedAt)
        {
            return false;
        }

        Stage = stage;
        StageChangedAt = at;
        return true;
    }
}
