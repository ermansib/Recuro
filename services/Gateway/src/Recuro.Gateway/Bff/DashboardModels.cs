namespace Recuro.Gateway.Bff;

/// <summary>The frontend's <c>DashboardData</c> (frontend/src/domain/types.ts), field for field.</summary>
internal sealed record DashboardData(
    string DateLabel,
    IReadOnlyList<DashboardStat> Stats,
    IReadOnlyList<TatBreachRow> TatBreaches,
    IReadOnlyList<PipelineStageCount> Pipeline,
    IReadOnlyList<KpiRow> Kpis,
    IReadOnlyList<UpcomingItem> Upcoming,
    string KpiPeriodLabel);

/// <summary>A tile. <c>tone</c> is '' | 'g' | 'a' | 'r' | 't'.</summary>
internal sealed record DashboardStat(string Label, string Value, string Trend, string Tone);

internal sealed record TatBreachRow(string ReqId, string Position, string Stage, string StageTone, string Escalation, string Link);

internal sealed record PipelineStageCount(string Stage, int Count, string Color);

internal sealed record KpiRow(string Name, string Target, string Value, string Status, string Tone, IReadOnlyList<double> Trend);

internal sealed record UpcomingItem(string Day, string Month, string Title, string Detail, string Link);

/// <summary>
/// What one service contributes to the dashboard: any subset of the <see cref="DashboardData"/> lists,
/// in the same shapes. Proposed contract: each owning service answers
/// <c>GET /api/v1/&lt;area&gt;/dashboard/ta</c> with this, scoped to the caller's permissions.
/// </summary>
internal sealed record DashboardFragment(
    IReadOnlyList<DashboardStat>? Stats,
    IReadOnlyList<TatBreachRow>? TatBreaches,
    IReadOnlyList<PipelineStageCount>? Pipeline,
    IReadOnlyList<KpiRow>? Kpis,
    IReadOnlyList<UpcomingItem>? Upcoming);
