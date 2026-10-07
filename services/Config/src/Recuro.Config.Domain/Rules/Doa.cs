namespace Recuro.Config.Domain.Rules;

/// <summary>Who decides a leg: a role whose inbox receives it, and how the route names that person.</summary>
public sealed record LegAssignee(string Role, string Label);

/// <summary>Where a breached leg escalates, and how many working days after the breach.</summary>
public sealed record EscalationStep(string Role, string Label, int AfterWorkingDays);

/// <summary>One approval leg of a route, with its SLA (§5.2) and escalation ladder (§5.4).</summary>
public sealed record ApprovalLeg(string Name, IReadOnlyList<LegAssignee> Assignees, int SlaWorkingDays, IReadOnlyList<EscalationStep> Escalation);

/// <summary>Overall TAT band for the level (the frontend's <c>overallTat</c>).</summary>
public sealed record TatRange(int MinDays, int MaxDays, string Label);

/// <summary>
/// §5.1 DOA route for one grade. The first fields are the frontend's <c>DoaRoute</c>; <see cref="Legs"/>
/// and <see cref="OobLegs"/> drive the workflow engine. Grades are tenant data, not an enum.
/// </summary>
public sealed record DoaRouteRule(
    string Grade,
    string Initiating,
    string Recommending,
    string Approving,
    string ApproverRole,
    string BandLabel,
    TatRange OverallTat,
    IReadOnlyList<ApprovalLeg> Legs,
    IReadOnlyList<ApprovalLeg> OobLegs);

public enum BudgetStatus
{
    /// <summary>Within the approved budget.</summary>
    In,

    /// <summary>Out of budget (REQ-003): the route gains the out-of-budget legs.</summary>
    Oob,
}

/// <summary>The DOA matrix (§5.1, Annexure F).</summary>
public sealed record DoaMatrix(IReadOnlyList<DoaRouteRule> Routes)
{
    public DoaRouteRule? Find(string grade) =>
        Routes.FirstOrDefault(r => string.Equals(r.Grade, grade, StringComparison.OrdinalIgnoreCase));

    /// <summary>The legs for <paramref name="budget"/>: out-of-budget routes add their extra legs at the end.</summary>
    public static IReadOnlyList<ApprovalLeg> LegsFor(DoaRouteRule route, BudgetStatus budget)
    {
        ArgumentNullException.ThrowIfNull(route);
        return budget == BudgetStatus.Oob ? [.. route.Legs, .. route.OobLegs] : route.Legs;
    }
}
