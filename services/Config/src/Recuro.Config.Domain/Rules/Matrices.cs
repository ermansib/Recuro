namespace Recuro.Config.Domain.Rules;

/// <summary>A role to escalate or notify, and how the policy names it.</summary>
public sealed record RoleContact(string Role, string Label);

/// <summary>§5.2 TAT standard for one stage, in working days.</summary>
public sealed record TatStage(string Stage, string Label, int MinWorkingDays, int MaxWorkingDays, string Owner, RoleContact? EscalateTo);

/// <summary>The TAT matrix (§5.2).</summary>
public sealed record TatMatrix(IReadOnlyList<TatStage> Stages);

/// <summary>§5.4 escalation path for one issue type.</summary>
public sealed record EscalationIssue(string Issue, string Label, RoleContact First, RoleContact Final);

/// <summary>The escalation matrix (§5.4).</summary>
public sealed record EscalationMatrix(IReadOnlyList<EscalationIssue> Issues);

/// <summary>Who approves one leg of an offer (the frontend's <c>{ label, approverRole }</c>).</summary>
public sealed record OfferApproval(string Label, string ApproverRole);

/// <summary>§5.3 offer approval rule: the frontend's <c>OfferMatrixRule</c>.</summary>
public sealed record OfferMatrixRule(IReadOnlyList<string> Levels, OfferApproval WithinBand, OfferApproval Deviation);

/// <summary>The offer approval matrix (§5.3, Annexure D).</summary>
public sealed record OfferMatrix(IReadOnlyList<OfferMatrixRule> Rules);

/// <summary>
/// §5.5 BGV check: the frontend's <c>BgvCheckRule</c>, plus an optional machine-readable
/// <see cref="AppliesWhen"/> for the Bgv service. Without it, Bgv applies its built-in FRD §5.5 rule.
/// </summary>
public sealed record BgvCheckRule(string Type, string Label, string Detail, string Condition, BgvAppliesWhen? AppliesWhen = null);

/// <summary>
/// When a check is run: on every hire (<see cref="Always"/>), or when the role's grade is in
/// <see cref="Grades"/> or the role carries any of <see cref="AnyFlags"/> (e.g. <c>customerFacing</c>).
/// </summary>
public sealed record BgvAppliesWhen(bool Always = false, IReadOnlyList<string>? Grades = null, IReadOnlyList<string>? AnyFlags = null);

/// <summary>The BGV applicability matrix (§5.5).</summary>
public sealed record BgvMatrix(IReadOnlyList<BgvCheckRule> Checks);
