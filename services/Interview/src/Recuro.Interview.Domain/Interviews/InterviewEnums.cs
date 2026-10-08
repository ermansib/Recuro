using Recuro.BuildingBlocks.Domain;

namespace Recuro.Interview.Domain.Interviews;

/// <summary>How the round is held. Names match the frontend's <c>InterviewRound.mode</c>.</summary>
public enum InterviewMode
{
    Telephonic,
    Video,
    InPerson,
}

/// <summary>Lifecycle of one scheduled round.</summary>
public enum InterviewStatus
{
    /// <summary>On the calendar, or held and waiting for feedback.</summary>
    Scheduled,

    /// <summary>Every panel member has submitted feedback.</summary>
    Completed,

    /// <summary>Called off, for example because the application left the Interview stage.</summary>
    Cancelled,
}

/// <summary>One interviewer's Annexure-B assessment (RCU-INT-002/003).</summary>
public enum AssessmentStatus
{
    /// <summary>Nothing saved yet. The frontend shows it as <c>Scheduled</c>.</summary>
    Pending,

    /// <summary>Autosaved, still editable.</summary>
    Draft,

    /// <summary>Final and locked; a change is a superseding revision with a reason.</summary>
    Submitted,
}

/// <summary>Overall recommendation. Names match the frontend's <c>Recommendation</c>.</summary>
public enum Recommendation
{
    StronglyRecommend,
    Recommend,
    Reservations,
    DoNotRecommend,
}

/// <summary>Legal moves, as data (FRD §6 style).</summary>
public static class InterviewTransitions
{
    public static TransitionTable<InterviewStatus> Rounds { get; } = new(new Dictionary<InterviewStatus, InterviewStatus[]>
    {
        [InterviewStatus.Scheduled] = [InterviewStatus.Completed, InterviewStatus.Cancelled],
        [InterviewStatus.Completed] = [],
        [InterviewStatus.Cancelled] = [],
    });

    public static TransitionTable<AssessmentStatus> Assessments { get; } = new(new Dictionary<AssessmentStatus, AssessmentStatus[]>
    {
        [AssessmentStatus.Pending] = [AssessmentStatus.Draft, AssessmentStatus.Submitted],
        [AssessmentStatus.Draft] = [AssessmentStatus.Draft, AssessmentStatus.Submitted],
        [AssessmentStatus.Submitted] = [],
    });
}

/// <summary>Column sizes and rule constants shared by validators and the EF configuration.</summary>
public static class InterviewLimits
{
    public const int IdLength = 64;
    public const int ShortText = 200;
    public const int LongText = 2000;
    public const int MaxPanel = 10;
    public const int MaxCompetencies = 30;
    public const int MaxFlags = 10;
    public const int MinScore = 1;
    public const int MaxScore = 5;
    public const int MinReasonLength = 10;
    public const int MinDurationMinutes = 15;
    public const int MaxDurationMinutes = 480;
}
