using Recuro.Config.Domain.Rules;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.RuleSets;

/// <summary>
/// The FRD §5 matrices, seeded exactly as shown there ("seed v1; any tenant variation is
/// configuration, not a fork"). DOA, offer and BGV match <c>frontend/src/mocks/data/rules.json</c>.
/// </summary>
public static class DefaultRuleSets
{
    /// <summary>The seed's effective date (rules.json <c>effectiveFrom</c>).</summary>
    public static readonly DateTimeOffset EffectiveFrom = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    private const string HrHead = "hrhead";
    private const string MdCeo = "mdceo";

    /// <summary>MRF approval TAT is 2 working days per leg (§5.2).</summary>
    private const int MrfLegSla = 2;

    public static IReadOnlyList<(MatrixType Type, object Matrix)> All =>
    [
        (MatrixType.Doa, Doa),
        (MatrixType.Tat, Tat),
        (MatrixType.Offer, Offer),
        (MatrixType.Escalation, Escalation),
        (MatrixType.Bgv, Bgv),
        (MatrixType.Calendar, Calendar),
    ];

    // §5.6 row 3: MRF SLA breach → HR Head, then MD/CEO 48 h (2 wd) later.
    private static readonly IReadOnlyList<EscalationStep> MrfEscalation =
        [new(HrHead, "HR Head", 0), new(MdCeo, "MD/CEO", 2)];

    private static readonly IReadOnlyList<EscalationStep> MdCeoReminder = [new(MdCeo, "MD/CEO", 0)];

    // REQ-003: out-of-budget demand needs an extra MD/CEO leg.
    private static readonly IReadOnlyList<ApprovalLeg> OobLegs =
        [new("Out-of-budget approval", [new(MdCeo, "MD/CEO (out of budget)")], MrfLegSla, MdCeoReminder)];

    public static DoaMatrix Doa { get; } = new(
    [
        Route("E", "Branch Manager", "Regional HR", "Zonal Business Head", HrHead, "₹3.5L – ₹6L", new(15, 20, "15–20 wd (junior)")),
        Route("M1", "Reporting Manager", "HOD", "Zonal Head + HR Head", HrHead, "₹6L – ₹12L", new(25, 35, "25–35 wd (managerial)")),
        Route("M3", "HOD", "Function Head", "HR Head", HrHead, "₹18L – ₹24L", new(25, 35, "25–35 wd (managerial)")),
        Route("VP", "Function Head", "HR Head", "MD/CEO", MdCeo, "₹30L – ₹45L", new(25, 35, "25–35 wd (managerial)")),
        Route("KMP", "MD/CEO", "HR Head + NRC", "Board / NRC", MdCeo, "Board-approved", new(45, 60, "45–60 wd (Sr Mgmt/KMP)")),
    ]);

    public static TatMatrix Tat { get; } = new(
    [
        new("mrf-approval", "MRF approval (raise → approved)", 2, 2, "HOD / HR", new(HrHead, "HR Head")),
        new("sourcing", "Sourcing & screening (shortlist ready)", 5, 7, "HR-TA", new(HrHead, "HR Head")),
        new("interview", "Interview scheduling → completion", 5, 5, "HR / Panel", new("hod", "HOD")),
        new("bgv", "BGV completion", 10, 15, "HR / BGV vendor", new(HrHead, "HR Head")),
        new("offer-issuance", "Offer issuance post-selection", 2, 2, "HR", new(HrHead, "HR Head")),
        new("offer-to-joining", "Offer → joining (notice-dependent)", 15, 60, "HR", null),
        new("onboarding-day1", "Onboarding Day-1 setup", 1, 1, "HR / IT / Admin", new(HrHead, "HR Head")),
        new("overall-junior", "Overall — Junior/Staff", 15, 20, "HR", null),
        new("overall-managerial", "Overall — Managerial", 25, 35, "HR", null),
        new("overall-kmp", "Overall — Sr Mgmt/KMP", 45, 60, "HR Head", new(MdCeo, "MD/CEO")),
    ]);

    public static OfferMatrix Offer { get; } = new(
    [
        new(["E", "M1"], new("Within band → HR-TA + HOD sign-off", HrHead), new("Deviation → HR Head approval", HrHead)),
        new(["M3", "VP"], new("Within band → HR Head sign-off", HrHead), new("Deviation → MD/CEO approval", MdCeo)),
        new(["KMP"], new("Within band → MD/CEO + NRC recommendation", MdCeo), new("Deviation → Board approval", MdCeo)),
    ]);

    public static EscalationMatrix Escalation { get; } = new(
    [
        new("tat-breach-sourcing", "TAT breach — sourcing/screening", new("ta-head", "TA-Head"), new(MdCeo, "MD/CEO")),
        new("adverse-bgv", "Adverse BGV finding", new(HrHead, "HR Head + Compliance"), new(MdCeo, "MD/CEO")),
        new("ctc-deviation", "CTC deviation beyond band", new(MdCeo, "MD/CEO"), new(MdCeo, "MD/CEO")),
        new("feedback-delay", "Panel non-availability / feedback delay", new("hod", "HOD"), new(HrHead, "HR Head")),
        new("candidate-grievance", "Candidate grievance", new("ta-head", "TA-Head"), new(MdCeo, "MD/CEO")),
        new("vendor-sla-breach", "Vendor/consultant SLA breach", new("ta-lead", "TA Lead"), new(HrHead, "HR Head")),
    ]);

    public static BgvMatrix Bgv { get; } = new(
    [
        new("identity", "Identity & Address", "National ID, tax ID + 1 address proof", "All employees — always"),
        new("education", "Educational Qualification", "Recognised-bureau verification", "All roles"),
        new("employment", "Employment / Reference", "Last 2 employers; salary & reason for leaving", "All lateral hires"),
        new("police", "Police / Antecedent", "Customer-facing / cash-handling roles", "Flagged roles"),
        new("credit", "Credit / Bureau Check", "Finance-function roles", "Credit, Collections, Finance + Fit & Proper"),
        new("court", "Court Record / Litigation", "Risk-based — senior & customer-facing", "Senior + customer-facing"),
        new("fitProper", "Fit & Proper Declaration", "Directors / KMP / Sr Mgmt only", "KMP / Senior Management"),
        new("social", "Social Media / Digital", "Optional — Sr Mgmt roles", "Senior Management (optional)"),
        new("coi", "Conflict of Interest", "Relationship with employees/Directors", "All"),
    ]);

    /// <summary>A Monday–Friday week with national holidays. Tenants add their own locations and dates.</summary>
    public static CalendarMatrix Calendar { get; } = new(
        "default",
        [
            new BusinessCalendar(
                "default",
                [DayOfWeek.Saturday, DayOfWeek.Sunday],
                [new(2026, 1, 26), new(2026, 8, 15), new(2026, 10, 2), new(2026, 12, 25), new(2027, 1, 26)]),
        ]);

    private static DoaRouteRule Route(string grade, string initiating, string recommending, string approving, string approverRole, string band, TatRange overall) =>
        new(
            grade,
            initiating,
            recommending,
            approving,
            approverRole,
            band,
            overall,
            [
                new("Recommend", [new(HrHead, recommending)], MrfLegSla, MrfEscalation),
                new("Approve", [new(approverRole, approving)], MrfLegSla, approverRole == MdCeo ? MdCeoReminder : MrfEscalation),
            ],
            OobLegs);
}
