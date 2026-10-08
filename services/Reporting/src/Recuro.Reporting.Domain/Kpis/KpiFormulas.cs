using Recuro.Reporting.Domain.Periods;
using Recuro.Reporting.Domain.Projections;
using Recuro.Reporting.Domain.Reports;

namespace Recuro.Reporting.Domain.Kpis;

/// <summary>
/// TAT targets resolved from Config's matrices, in working days: each grade's overall band (DOA matrix
/// <c>overallTat.maxDays</c>) and each stage's maximum (TAT matrix). Reporting never keeps its own copy.
/// </summary>
/// <param name="OverallByGrade">Overall TAT maximum per grade.</param>
/// <param name="Stages">TAT matrix stage maximums, by stage key (<c>mrf-approval</c>, <c>overall-managerial</c>, ...).</param>
/// <param name="ConfigVersion">The Config versions the targets came from, for the snapshot.</param>
public sealed record TatTargets(
    IReadOnlyDictionary<string, int> OverallByGrade,
    IReadOnlyDictionary<string, int> Stages,
    string? ConfigVersion)
{
    /// <summary>Stage used when a hire's grade is unknown.</summary>
    public const string DefaultOverallStage = "overall-managerial";

    /// <summary>
    /// The FRD §5.2 values, used only while Config can't be reached (the same fallback Pipeline uses), so a
    /// report still renders. Tenants' own values always come from Config.
    /// </summary>
    public static TatTargets FrdFallback { get; } = new(
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["mrf-approval"] = 2,
            ["overall-junior"] = 20,
            ["overall-managerial"] = 35,
            ["overall-kmp"] = 60,
        },
        null);

    /// <summary>Overall TAT for a grade, in working days.</summary>
    public int? Overall(string? grade) =>
        grade is not null && OverallByGrade.TryGetValue(grade, out var days) ? days
        : Stages.TryGetValue(DefaultOverallStage, out var fallback) ? fallback
        : null;

    public int? Stage(string stage) => Stages.TryGetValue(stage, out var days) ? days : null;

    /// <summary>
    /// TAT standards are working days while durations are measured on the clock, so a target is stretched
    /// by 7/5 to calendar days. Holidays are not counted here: Config owns the calendar.
    /// </summary>
    public static decimal ToCalendarDays(decimal workingDays) => Math.Round(workingDays * 7m / 5m, 1);
}

/// <summary>What the formulas read: the period, the projections and the targets.</summary>
public sealed record KpiInputs(
    ReportPeriod Period,
    ReportWindow Window,
    DateTimeOffset Now,
    IReadOnlyList<ApplicationFact> Applications,
    IReadOnlyDictionary<string, RequisitionFact> Requisitions,
    IReadOnlyList<FeedbackFact> Feedback,
    IReadOnlyList<RecruitmentCost> Costs,
    TatTargets Targets)
{
    /// <summary>Hires in the period: offers accepted inside the window.</summary>
    public IReadOnlyList<ApplicationFact> Hires { get; } = Applications.Where(a => Window.Contains(a.OfferAcceptedAt)).ToList();

    public IReadOnlyList<RecruitmentCost> PeriodCosts { get; } =
        Costs.Where(c => c.Month >= Period.FirstDay && c.Month < Period.EndExclusive).ToList();

    public RequisitionFact? RequisitionOf(ApplicationFact application) =>
        application?.ReqId is { } reqId && Requisitions.TryGetValue(reqId, out var requisition) ? requisition : null;
}

/// <summary>One source channel's share of the period's hires and spend (RCU-RPT-003).</summary>
public sealed record MixShare(string Source, int Hires, decimal Percent, decimal? Cost, decimal? CostPerHire);

/// <summary>A formula's result before status and formatting.</summary>
/// <param name="Actual">The value, or null when there is nothing to measure.</param>
/// <param name="Sample">How many records it was computed from.</param>
/// <param name="Target">The resolved target in the metric's unit, if it has one.</param>
/// <param name="Mix">Source Mix breakdown, largest share first.</param>
/// <param name="Currency">ISO currency of a money value.</param>
public sealed record KpiMeasurement(
    decimal? Actual,
    int Sample,
    decimal? Target,
    IReadOnlyList<MixShare>? Mix = null,
    string? Currency = null);

/// <summary>Pass/fail of a measurement against its definition.</summary>
public enum KpiStatus
{
    OnTrack,
    Near,
    OffTrack,
    Review,
    NoData,
}

/// <summary>A §12 formula (Strategy). The registry's <see cref="MetricDefinition.Key"/> selects one.</summary>
public interface IKpiFormula
{
    string Key { get; }

    KpiMeasurement Measure(KpiInputs inputs, MetricDefinition definition);
}

/// <summary>Every formula, by key, and the status rule shared by all of them.</summary>
public static class KpiFormulas
{
    private static readonly Dictionary<string, IKpiFormula> Formulas = new IKpiFormula[]
    {
        new TimeToFillFormula(),
        new TimeToHireFormula(),
        new CostPerHireFormula(),
        new OfferToJoinFormula(),
        new AwaitingFeedFormula(MetricKeys.QualityOfHire),
        new AwaitingFeedFormula(MetricKeys.EarlyAttrition),
        new FeedbackTatFormula(),
        new SourceMixFormula(),
        new MrfTatFormula(),
    }.ToDictionary(f => f.Key, StringComparer.Ordinal);

    public static bool IsKnown(string key) => Formulas.ContainsKey(key);

    public static KpiMeasurement Measure(KpiInputs inputs, MetricDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(definition);
        return Formulas.TryGetValue(definition.Key, out var formula)
            ? formula.Measure(inputs, definition)
            : new KpiMeasurement(null, 0, null);
    }

    public static KpiStatus Evaluate(MetricDefinition definition, KpiMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(measurement);
        if (measurement.Sample == 0 || (measurement.Actual is null && definition.Unit != MetricUnit.Mix))
        {
            return KpiStatus.NoData;
        }

        if (definition.Direction == TargetDirection.Review || measurement.Target is not { } target || measurement.Actual is not { } actual)
        {
            return KpiStatus.Review;
        }

        var slack = Math.Abs(target) * definition.NearPercent / 100m;
        return definition.Direction == TargetDirection.AtMost
            ? actual <= target ? KpiStatus.OnTrack : actual <= target + slack ? KpiStatus.Near : KpiStatus.OffTrack
            : actual >= target ? KpiStatus.OnTrack : actual >= target - slack ? KpiStatus.Near : KpiStatus.OffTrack;
    }

    /// <summary>A fixed target, or one sourced from Config's TAT for a set of records (averaged).</summary>
    internal static decimal? ResolveTarget(MetricDefinition definition, TatTargets targets, IEnumerable<string?> grades)
    {
        if (definition.TargetSource is not { } source)
        {
            return definition.Target;
        }

        IEnumerable<int?> workingDays = source == MetricKeys.OverallTatSource
            ? grades.Select(targets.Overall)
            : source.StartsWith(MetricKeys.TatStagePrefix, StringComparison.Ordinal)
                ? [targets.Stage(source[MetricKeys.TatStagePrefix.Length..])]
                : [];
        var known = workingDays.OfType<int>().ToList();
        return known.Count == 0
            ? null
            : TatTargets.ToCalendarDays((decimal)known.Average() * definition.TargetFactor);
    }

    internal static decimal Days(DateTimeOffset from, DateTimeOffset to) => (decimal)(to - from).TotalDays;

    internal static decimal? Average(IReadOnlyCollection<decimal> values) =>
        values.Count == 0 ? null : Math.Round(values.Average(), 1);

    internal static decimal? Percent(int part, int whole) =>
        whole == 0 ? null : Math.Round(part * 100m / whole, 1);
}

internal sealed class TimeToFillFormula : IKpiFormula
{
    public string Key => MetricKeys.TimeToFill;

    public KpiMeasurement Measure(KpiInputs inputs, MetricDefinition definition)
    {
        var hires = inputs.Hires
            .Select(h => (Hire: h, Requisition: inputs.RequisitionOf(h)))
            .Where(x => x.Requisition?.ApprovedAt is not null)
            .ToList();
        var days = hires.Select(x => KpiFormulas.Days(x.Requisition!.ApprovedAt!.Value, x.Hire.OfferAcceptedAt!.Value)).ToList();
        var target = KpiFormulas.ResolveTarget(definition, inputs.Targets, hires.Select(x => x.Requisition!.Grade));
        return new KpiMeasurement(KpiFormulas.Average(days), days.Count, target);
    }
}

internal sealed class TimeToHireFormula : IKpiFormula
{
    public string Key => MetricKeys.TimeToHire;

    public KpiMeasurement Measure(KpiInputs inputs, MetricDefinition definition)
    {
        var hires = inputs.Hires.Where(h => h.CreatedAt is not null).ToList();
        var days = hires.Select(h => KpiFormulas.Days(h.CreatedAt!.Value, h.OfferAcceptedAt!.Value)).ToList();
        var target = KpiFormulas.ResolveTarget(definition, inputs.Targets, hires.Select(h => inputs.RequisitionOf(h)?.Grade));
        return new KpiMeasurement(KpiFormulas.Average(days), days.Count, target);
    }
}

internal sealed class CostPerHireFormula : IKpiFormula
{
    public string Key => MetricKeys.CostPerHire;

    public KpiMeasurement Measure(KpiInputs inputs, MetricDefinition definition)
    {
        var costs = inputs.PeriodCosts;
        var hires = inputs.Hires.Count;
        if (costs.Count == 0 || hires == 0)
        {
            return new KpiMeasurement(null, hires, definition.Target);
        }

        var currency = costs.GroupBy(c => c.Currency).OrderByDescending(g => g.Sum(c => c.Amount)).First().Key;
        var spend = costs.Where(c => c.Currency == currency).Sum(c => c.Amount);
        return new KpiMeasurement(Math.Round(spend / hires, 0), hires, definition.Target, Currency: currency);
    }
}

internal sealed class OfferToJoinFormula : IKpiFormula
{
    public string Key => MetricKeys.OfferToJoin;

    public KpiMeasurement Measure(KpiInputs inputs, MetricDefinition definition)
    {
        // Only offers whose joining date has passed can have joined; later ones are still pending.
        var today = DateOnly.FromDateTime(inputs.Now.UtcDateTime);
        var due = inputs.Hires
            .Where(h => (h.JoiningDate ?? DateOnly.FromDateTime(h.OfferAcceptedAt!.Value.UtcDateTime)) <= today || h.JoinedAt is not null)
            .ToList();
        var joined = due.Count(h => h.JoinedAt is not null);
        return new KpiMeasurement(KpiFormulas.Percent(joined, due.Count), due.Count, definition.Target);
    }
}

/// <summary>
/// Quality of Hire (first performance review, RCU-RPT-006) and Early Attrition (exits) need feeds that
/// no service publishes yet, so they report "no data" instead of a made-up number.
/// </summary>
internal sealed class AwaitingFeedFormula(string key) : IKpiFormula
{
    public string Key => key;

    public KpiMeasurement Measure(KpiInputs inputs, MetricDefinition definition) => new(null, 0, definition.Target);
}

internal sealed class FeedbackTatFormula : IKpiFormula
{
    public string Key => MetricKeys.FeedbackTat;

    public KpiMeasurement Measure(KpiInputs inputs, MetricDefinition definition)
    {
        var counted = inputs.Feedback.Where(f => inputs.Window.Contains(f.CountsAt)).ToList();
        var onTime = counted.Count(f => f.OnTime);
        return new KpiMeasurement(KpiFormulas.Percent(onTime, counted.Count), counted.Count, definition.Target);
    }
}

internal sealed class SourceMixFormula : IKpiFormula
{
    public const string Unknown = "unknown";

    public string Key => MetricKeys.SourceMix;

    public KpiMeasurement Measure(KpiInputs inputs, MetricDefinition definition)
    {
        var hires = inputs.Hires;
        var costs = inputs.PeriodCosts;
        var currency = costs.GroupBy(c => c.Currency).OrderByDescending(g => g.Sum(c => c.Amount)).FirstOrDefault()?.Key;
        var costBySource = costs.Where(c => c.Currency == currency)
            .GroupBy(c => Normalise(c.Source))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount), StringComparer.Ordinal);
        var hiresBySource = hires.GroupBy(h => Normalise(h.Source)).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var mix = hiresBySource.Keys.Union(costBySource.Keys)
            .Select(source =>
            {
                var count = hiresBySource.GetValueOrDefault(source);
                decimal? cost = costBySource.TryGetValue(source, out var amount) ? amount : null;
                return new MixShare(
                    source,
                    count,
                    KpiFormulas.Percent(count, hires.Count) ?? 0m,
                    cost,
                    cost is { } c && count > 0 ? Math.Round(c / count, 0) : null);
            })
            .OrderByDescending(s => s.Hires)
            .ThenBy(s => s.Source, StringComparer.Ordinal)
            .ToList();
        var top = mix.FirstOrDefault(s => s.Hires > 0);
        return new KpiMeasurement(top?.Percent, hires.Count, null, mix, currency);
    }

    /// <summary>Channels are compared case-insensitively; a hire with no source shows as <c>unknown</c>.</summary>
    public static string Normalise(string? source) =>
        string.IsNullOrWhiteSpace(source) ? Unknown : source.Trim().ToLowerInvariant();
}

internal sealed class MrfTatFormula : IKpiFormula
{
    public string Key => MetricKeys.MrfTat;

    public KpiMeasurement Measure(KpiInputs inputs, MetricDefinition definition)
    {
        var approved = inputs.Requisitions.Values
            .Where(r => inputs.Window.Contains(r.ApprovedAt) && r.SubmittedAt is not null && r.SubmittedAt <= r.ApprovedAt)
            .ToList();
        var days = approved.Select(r => KpiFormulas.Days(r.SubmittedAt!.Value, r.ApprovedAt!.Value)).ToList();
        var target = KpiFormulas.ResolveTarget(definition, inputs.Targets, approved.Select(r => r.Grade));
        return new KpiMeasurement(KpiFormulas.Average(days), days.Count, target);
    }
}
