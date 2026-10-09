using Recuro.BuildingBlocks.Domain;

namespace Recuro.Reporting.Domain.Kpis;

/// <summary>What a metric's number is.</summary>
public enum MetricUnit
{
    Days,
    Percent,
    Money,

    /// <summary>A breakdown with no single number (Source Mix).</summary>
    Mix,
}

/// <summary>Which side of the target is good.</summary>
public enum TargetDirection
{
    AtMost,
    AtLeast,

    /// <summary>No pass/fail: the metric is reviewed (Source Mix, or CPH without a budget).</summary>
    Review,
}

/// <summary>
/// One §12 metric as the tenant defines it (RCU-RPT-002). <see cref="Key"/> picks the formula; the rest
/// is data: name, tooltip, target and how close to the target counts as "near".
/// </summary>
/// <param name="Key">Formula key, one of <see cref="MetricKeys"/>.</param>
/// <param name="Name">Display name, e.g. <c>Offer-to-Join Ratio</c>.</param>
/// <param name="Description">The definition shown in the tooltip.</param>
/// <param name="Unit">What the number is.</param>
/// <param name="Direction">Which side of the target is good.</param>
/// <param name="TargetLabel">How the target reads, e.g. <c>Target ≥ 85%</c>.</param>
/// <param name="Target">Fixed target. Ignored when <paramref name="TargetSource"/> is set.</param>
/// <param name="TargetSource">
/// Where a TAT-based target comes from: <c>doa:overall</c> (each hire's grade band in Config's DOA
/// matrix) or <c>tat:&lt;stage&gt;</c> (a stage of Config's TAT matrix).
/// </param>
/// <param name="TargetFactor">Multiplies a sourced target, e.g. 0.8 for "80% of TAT matrix".</param>
/// <param name="NearPercent">How far past the target (in % of it) is still "near" rather than off track.</param>
/// <param name="Dashboard">Shown on the TA dashboard's KPI card.</param>
public sealed record MetricDefinition(
    string Key,
    string Name,
    string Description,
    MetricUnit Unit,
    TargetDirection Direction,
    string TargetLabel,
    decimal? Target = null,
    string? TargetSource = null,
    decimal TargetFactor = 1m,
    decimal NearPercent = 10m,
    bool Dashboard = false);

/// <summary>The nine §12 formula keys (RCU-RPT-001 / FRD §12).</summary>
public static class MetricKeys
{
    public const string TimeToFill = "time-to-fill";
    public const string TimeToHire = "time-to-hire";
    public const string CostPerHire = "cost-per-hire";
    public const string OfferToJoin = "offer-to-join";
    public const string QualityOfHire = "quality-of-hire";
    public const string EarlyAttrition = "early-attrition";
    public const string FeedbackTat = "feedback-tat";
    public const string SourceMix = "source-mix";
    public const string MrfTat = "mrf-tat";

    public static readonly IReadOnlyList<string> All =
        [TimeToFill, TimeToHire, CostPerHire, OfferToJoin, QualityOfHire, EarlyAttrition, FeedbackTat, SourceMix, MrfTat];

    public const string OverallTatSource = "doa:overall";
    public const string TatStagePrefix = "tat:";
}

/// <summary>
/// A versioned set of metric definitions (RCU-RPT-002 "metric-definition registry, versioned"). A new
/// version applies from <see cref="EffectiveFrom"/>; older periods keep the version that applied then.
/// </summary>
public sealed class MetricDefinitionSet : Entity, ITenantOwned
{
    private MetricDefinitionSet()
    {
    }

    public Guid TenantId { get; private set; }

    public int Version { get; private set; }

    public DateTimeOffset EffectiveFrom { get; private set; }

    public IReadOnlyList<MetricDefinition> Definitions { get; private set; } = [];

    public string CreatedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static MetricDefinitionSet Publish(int version, DateTimeOffset effectiveFrom, IReadOnlyList<MetricDefinition> definitions, string createdBy, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        Version = version,
        EffectiveFrom = effectiveFrom,
        Definitions = definitions,
        CreatedBy = createdBy,
        CreatedAt = at,
    };
}

/// <summary>
/// The FRD §12 metric register as shipped (prototype S-15), used until a tenant publishes its own
/// version. Wording and targets are defaults, not rules: tenants change them through the registry.
/// </summary>
public static class DefaultMetricDefinitions
{
    public const int Version = 0;

    public static IReadOnlyList<MetricDefinition> All { get; } =
    [
        new(MetricKeys.TimeToFill, "Time to Fill", "Days from MRF approval to offer acceptance, averaged over hires in the period.", MetricUnit.Days, TargetDirection.AtMost, "Per TAT matrix", TargetSource: MetricKeys.OverallTatSource),
        new(MetricKeys.TimeToHire, "Time to Hire", "Days from application to offer acceptance, averaged over hires in the period.", MetricUnit.Days, TargetDirection.AtMost, "80% of TAT matrix", TargetSource: MetricKeys.OverallTatSource, TargetFactor: 0.8m, Dashboard: true),
        new(MetricKeys.CostPerHire, "Cost per Hire", "Recorded recruitment spend in the period divided by hires in the period.", MetricUnit.Money, TargetDirection.Review, "Quarterly budget", Dashboard: true),
        new(MetricKeys.OfferToJoin, "Offer-to-Join Ratio", "Share of offers accepted in the period (joining date passed) whose candidate joined.", MetricUnit.Percent, TargetDirection.AtLeast, "Target ≥ 85%", Target: 85m, NearPercent: 5m, Dashboard: true),
        new(MetricKeys.QualityOfHire, "Quality of Hire", "Share of the period's joiners rated meets-or-above at first performance review (needs the performance-system feed).", MetricUnit.Percent, TargetDirection.AtLeast, "Target ≥ 80%", Target: 80m, NearPercent: 5m, Dashboard: true),
        new(MetricKeys.EarlyAttrition, "Early Attrition (6-mo)", "Share of joiners who left within six months of joining (needs the HRMS exit feed).", MetricUnit.Percent, TargetDirection.AtMost, "Target ≤ 10%", Target: 10m, Dashboard: true),
        new(MetricKeys.FeedbackTat, "Panel Feedback ≤ 48h", "Share of interviewer feedback submitted within the SLA; feedback that went overdue unsubmitted counts as late.", MetricUnit.Percent, TargetDirection.AtLeast, "Target ≥ 90%", Target: 90m, NearPercent: 5m, Dashboard: true),
        new(MetricKeys.SourceMix, "Source Mix Effectiveness", "Share of hires and cost per hire by source channel, reviewed monthly by Head-TA.", MetricUnit.Mix, TargetDirection.Review, "Monthly by Head-TA"),
        new(MetricKeys.MrfTat, "MRF TAT (approval stage)", "Days from MRF submission to approval, averaged over approvals in the period.", MetricUnit.Days, TargetDirection.AtMost, "2 working days", TargetSource: MetricKeys.TatStagePrefix + "mrf-approval"),
    ];

    /// <summary>Dashboard order of the prototype's KPI card.</summary>
    public static IReadOnlyList<string> DashboardOrder { get; } =
        [MetricKeys.OfferToJoin, MetricKeys.FeedbackTat, MetricKeys.EarlyAttrition, MetricKeys.TimeToHire, MetricKeys.QualityOfHire, MetricKeys.CostPerHire];
}
