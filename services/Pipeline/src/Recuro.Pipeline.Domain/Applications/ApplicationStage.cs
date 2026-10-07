using Recuro.BuildingBlocks.Domain;

namespace Recuro.Pipeline.Domain.Applications;

/// <summary>Application states (FRD §6.2), same names as the frontend's <c>ApplicationStage</c>.</summary>
public enum ApplicationStage
{
    Sourced,
    Screened,
    Interview,
    Selection,
    BGV,
    Offer,
    PreBoarding,
    Onboarded,
    Confirmed,
    Rejected,
    Withdrawn,
    Hold,
}

/// <summary>
/// RCU-PPL-002: legal moves as data, identical to <c>applicationTransitions</c> in
/// frontend/src/domain/stateMachines.ts.
/// </summary>
public static class ApplicationTransitions
{
    private static readonly ApplicationStage[] TerminalExits = [ApplicationStage.Rejected, ApplicationStage.Withdrawn, ApplicationStage.Hold];

    public static readonly TransitionTable<ApplicationStage> Table = new(new Dictionary<ApplicationStage, ApplicationStage[]>
    {
        [ApplicationStage.Sourced] = [ApplicationStage.Screened, .. TerminalExits],
        [ApplicationStage.Screened] = [ApplicationStage.Interview, .. TerminalExits],
        [ApplicationStage.Interview] = [ApplicationStage.Selection, .. TerminalExits],
        [ApplicationStage.Selection] = [ApplicationStage.BGV, .. TerminalExits],
        [ApplicationStage.BGV] = [ApplicationStage.Offer, .. TerminalExits],
        [ApplicationStage.Offer] = [ApplicationStage.PreBoarding, .. TerminalExits],
        [ApplicationStage.PreBoarding] = [ApplicationStage.Onboarded, ApplicationStage.Withdrawn],
        [ApplicationStage.Onboarded] = [ApplicationStage.Confirmed],
        [ApplicationStage.Confirmed] = [],
        [ApplicationStage.Rejected] = [],
        [ApplicationStage.Withdrawn] = [],
        [ApplicationStage.Hold] =
        [
            ApplicationStage.Sourced, ApplicationStage.Screened, ApplicationStage.Interview, ApplicationStage.Selection,
            ApplicationStage.BGV, ApplicationStage.Offer, ApplicationStage.Rejected, ApplicationStage.Withdrawn,
        ],
    });

    /// <summary>Kanban columns on the pipeline board (S-06), in order; same as the frontend's <c>pipelineColumns</c>.</summary>
    public static readonly IReadOnlyList<ApplicationStage> BoardColumns =
    [
        ApplicationStage.Sourced, ApplicationStage.Screened, ApplicationStage.Interview,
        ApplicationStage.Selection, ApplicationStage.BGV, ApplicationStage.Offer,
    ];

    /// <summary>Stages an application can no longer leave.</summary>
    public static bool IsClosed(ApplicationStage stage) => Table.AllowedFrom(stage).Count == 0;
}
