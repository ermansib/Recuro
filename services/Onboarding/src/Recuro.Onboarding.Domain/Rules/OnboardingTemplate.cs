using Recuro.Onboarding.Domain.Cases;

namespace Recuro.Onboarding.Domain.Rules;

/// <summary>One Day-1 checklist item (Annexure E).</summary>
public sealed record ChecklistTemplateItem(string Key, string Label);

/// <summary>One §13 document and whether the file can be completed without it.</summary>
public sealed record DocumentTemplate(string Type, string Label, bool Mandatory);

/// <summary>
/// The tenant's onboarding rules: the Day-1 checklist, the §13 document list and the §9.9/§9.10
/// timings. They come from Config (<c>resolve/matrices/onboarding</c>); until Config carries that
/// matrix, <see cref="Default"/> is the FRD's own table. A case pins the version it was planned from.
/// </summary>
public sealed record OnboardingTemplate(
    string VersionId,
    IReadOnlyList<ChecklistTemplateItem> Checklist,
    IReadOnlyList<DocumentTemplate> Documents,
    IReadOnlyList<int> EngagementDaysBefore,
    int ProvisioningWorkingDaysBefore,
    int ProbationMonths,
    int CheckInDay,
    int ReviewDay,
    int ReviewWindowEndDay)
{
    /// <summary>Pinned on cases planned from the built-in FRD table rather than a Config version.</summary>
    public const string BuiltInVersion = "frd-annexure-e";

    /// <summary>FRD Annexure E (11 items), §13 documents, T-21/T-7 touchpoints, T-5 working-day IT ticket, 6-month probation.</summary>
    public static OnboardingTemplate Default { get; } = new(
        BuiltInVersion,
        [
            new("offer-acceptance", "Offer letter & signed acceptance on file"),
            new("bgv-report", "BGV report received & cleared"),
            new("identity-verified", "Identity, address & education verified"),
            new("statutory-registration", "PF / ESI registration completed"),
            new("bank-details", "Bank details captured for payroll"),
            new("id-card", "ID card issued"),
            new("system-access", "System / email access provisioned"),
            new("workstation", "Workstation / asset issued"),
            new("policies-signed", "CoC, POSH & Confidentiality signed"),
            new("induction-schedule", "Induction schedule shared"),
            new("manager-buddy", "Reporting manager & buddy assigned"),
        ],
        [
            new("identity-proof", "Identity proof", true),
            new("address-proof", "Address proof", true),
            new("education-certificates", "Educational certificates", true),
            new("photographs", "Passport-size photographs", true),
            new("bank-details", "Bank details (cancelled cheque)", true),
            new("signed-declarations", "Signed Code of Conduct, POSH & confidentiality declarations", true),
            new("relieving-letter", "Relieving letter from last employer", false),
            new("salary-slips", "Last 3 salary slips", false),
            new("tax-statement", "Previous employer tax statement (Form 16)", false),
        ],
        [21, 7],
        5,
        6,
        30,
        60,
        90);

    /// <summary>§9.10 milestones for one probation cycle, from its start (the joining date) to its end.</summary>
    public IReadOnlyList<PlannedMilestone> ProbationMilestones(DateOnly joiningDate, DateOnly probationEndsOn) =>
    [
        new(MilestoneKinds.CheckIn, $"Day-{CheckInDay} check-in", MilestonePhase.Probation, joiningDate.AddDays(CheckInDay)),
        new(MilestoneKinds.Review, $"Day {ReviewDay}–{ReviewWindowEndDay} probation review", MilestonePhase.Probation, joiningDate.AddDays(ReviewDay)),
        new(MilestoneKinds.ProbationEnd, "End of probation: confirm or extend", MilestonePhase.Probation, probationEndsOn),
    ];
}

/// <summary>A milestone to schedule: what, which phase, and the date it falls due.</summary>
public sealed record PlannedMilestone(string Kind, string Label, MilestonePhase Phase, DateOnly DueOn);
