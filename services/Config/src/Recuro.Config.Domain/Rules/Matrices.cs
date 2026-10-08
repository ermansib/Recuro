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

/// <summary>A tenant compensation rule (RCU-OFF-001): a CTC component's share of the total, in percent.</summary>
public sealed record CtcRule(string Id, string Component, decimal? MinPercent, decimal? MaxPercent);

/// <summary>
/// The offer approval matrix (§5.3, Annexure D), plus the offer policy the Offer service applies:
/// CTC structure rules, how long an offer stays open and when unaccepted offers are chased (RCU-OFF-006).
/// </summary>
public sealed record OfferMatrix(
    IReadOnlyList<OfferMatrixRule> Rules,
    IReadOnlyList<CtcRule>? CtcRules = null,
    int ValidityWorkingDays = OfferMatrix.DefaultValidityWorkingDays,
    int FirstChaseAfterWorkingDays = OfferMatrix.DefaultFirstChaseAfterWorkingDays,
    int ChaseEveryDays = OfferMatrix.DefaultChaseEveryDays)
{
    public const int DefaultValidityWorkingDays = 5;
    public const int DefaultFirstChaseAfterWorkingDays = 3;
    public const int DefaultChaseEveryDays = 7;
}

/// <summary>One interview round in a template, e.g. <c>{ type: "functional", label: "Functional" }</c>.</summary>
public sealed record InterviewRoundTemplate(string Type, string Label);

/// <summary>The rounds a grade goes through, in order (RCU-ASM-001).</summary>
public sealed record InterviewTemplate(string Grade, IReadOnlyList<InterviewRoundTemplate> Rounds);

/// <summary>The feedback SLA after an interview (RCU-ASM-004): remind the interviewer, then escalate.</summary>
public sealed record FeedbackPolicy(int ReminderAfterHours, int OverdueAfterHours);

/// <summary>Which grades need the selection ratified, by whom and how fast (RCU-ASM-006).</summary>
public sealed record RatificationRule(IReadOnlyList<string> Grades, string Role, string Label, int SlaWorkingDays);

/// <summary>The interview matrix.</summary>
public sealed record InterviewMatrix(IReadOnlyList<InterviewTemplate> Templates, FeedbackPolicy Feedback, RatificationRule Ratification);

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

/// <summary>One Day-1 checklist item (Annexure E, RCU-ONB-002).</summary>
public sealed record OnboardingChecklistItem(string Key, string Label);

/// <summary>One §13 joining document; the file can't be completed while a mandatory one is missing (RCU-ONB-003).</summary>
public sealed record OnboardingDocument(string Type, string Label, bool Mandatory);

/// <summary>
/// The onboarding matrix: the Day-1 checklist, the §13 document list, the §9.9 pre-boarding touchpoints
/// (engagement calls and the IT/Admin ticket before joining) and the §9.10 probation timings.
/// </summary>
public sealed record OnboardingMatrix(
    IReadOnlyList<OnboardingChecklistItem> Checklist,
    IReadOnlyList<OnboardingDocument> Documents,
    IReadOnlyList<int> EngagementDaysBefore,
    int ProvisioningWorkingDaysBefore,
    int ProbationMonths,
    int CheckInDay,
    int ReviewDay,
    int ReviewWindowEndDay);
