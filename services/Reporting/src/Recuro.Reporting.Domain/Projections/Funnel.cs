using Recuro.Reporting.Domain.Periods;

namespace Recuro.Reporting.Domain.Projections;

/// <summary>One funnel step: how many applications got at least this far, and the share of the step before.</summary>
public sealed record FunnelStep(FunnelStage Stage, int Count, decimal? ConversionPercent);

/// <summary>RCU-RPT-004: the six-stage funnel for applications created in a window.</summary>
public static class Funnel
{
    public static IReadOnlyList<FunnelStep> Count(IEnumerable<ApplicationFact> applications, ReportWindow window)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(window);
        var cohort = applications.Where(a => window.Contains(a.CreatedAt)).ToList();
        var steps = new List<FunnelStep>();
        int? previous = null;
        foreach (var stage in Enum.GetValues<FunnelStage>())
        {
            var count = cohort.Count(a => a.FurthestStage >= stage);
            decimal? conversion = previous is { } before ? before == 0 ? null : Math.Round(count * 100m / before, 1) : null;
            steps.Add(new FunnelStep(stage, count, conversion));
            previous = count;
        }

        return steps;
    }
}
