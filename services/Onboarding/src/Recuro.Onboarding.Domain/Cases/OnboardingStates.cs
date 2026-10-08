using Recuro.BuildingBlocks.Domain;

namespace Recuro.Onboarding.Domain.Cases;

/// <summary>
/// Where an onboarding case is (FRD §9.9–9.10). Pre-boarding starts on offer acceptance; Day-1 Ready
/// once the Annexure E checklist is complete; Confirmed when probation ends. Cancelled when the offer is
/// withdrawn after acceptance (saga compensation, §6.3).
/// </summary>
public enum CaseStatus
{
    PreBoarding,
    Day1Ready,
    Confirmed,
    Cancelled,
}

/// <summary>The case's state machine as data, so the rules live in one table.</summary>
public static class CaseTransitions
{
    public static readonly TransitionTable<CaseStatus> Table = new(new Dictionary<CaseStatus, CaseStatus[]>
    {
        [CaseStatus.PreBoarding] = [CaseStatus.Day1Ready, CaseStatus.Cancelled],
        [CaseStatus.Day1Ready] = [CaseStatus.Confirmed, CaseStatus.Cancelled],
        [CaseStatus.Confirmed] = [],
        [CaseStatus.Cancelled] = [],
    });

    public static bool IsClosed(CaseStatus status) => status is CaseStatus.Confirmed or CaseStatus.Cancelled;
}

/// <summary>Which part of the journey a milestone belongs to.</summary>
public enum MilestonePhase
{
    /// <summary>§9.9: joining instructions, engagement touchpoints, IT/Admin provisioning.</summary>
    PreBoarding,

    /// <summary>§9.10: Day-30 check-in, 60–90 review, end of probation.</summary>
    Probation,
}

/// <summary>A scheduled step: waits for its date, is raised (reminders go out), then completed.</summary>
public enum MilestoneStatus
{
    Scheduled,
    Due,
    Done,
    Cancelled,
}

public static class MilestoneTransitions
{
    public static readonly TransitionTable<MilestoneStatus> Table = new(new Dictionary<MilestoneStatus, MilestoneStatus[]>
    {
        [MilestoneStatus.Scheduled] = [MilestoneStatus.Due, MilestoneStatus.Done, MilestoneStatus.Cancelled],
        [MilestoneStatus.Due] = [MilestoneStatus.Done, MilestoneStatus.Cancelled],
        [MilestoneStatus.Done] = [],
        [MilestoneStatus.Cancelled] = [],
    });
}

/// <summary>The milestone keys other services see in <c>onboarding.milestone.due.v1</c>.</summary>
public static class MilestoneKinds
{
    public const string JoiningInstructions = "joining-instructions";
    public const string EngagementPrefix = "engagement-t";
    public const string ItProvisioning = "it-provisioning";
    public const string CheckIn = "probation-check-in";
    public const string Review = "probation-review";
    public const string ProbationEnd = "probation-end";

    /// <summary>Engagement touchpoints are keyed by their offset, e.g. <c>engagement-t21</c>.</summary>
    public static string Engagement(int daysBefore) => FormattableString.Invariant($"{EngagementPrefix}{daysBefore}");
}

/// <summary>§13 document state: uploaded files wait for HR Ops to verify them.</summary>
public enum DocumentStatus
{
    Missing,
    Uploaded,
    Verified,
    Rejected,
}

/// <summary>RCU-ONB-005 outcome at the end of probation.</summary>
public enum ProbationOutcome
{
    Confirm,
    Extend,
}

/// <summary>
/// What Onboarding knows about the hire's background verification, from <c>bgv.*</c> events. A pending
/// BGV (conditional offer, BGV-009) blocks confirmation, not joining (RCU-ONB-003).
/// </summary>
public enum BgvStatus
{
    NotStarted,
    Pending,
    UnderReview,
    Cleared,
    Adverse,
}
