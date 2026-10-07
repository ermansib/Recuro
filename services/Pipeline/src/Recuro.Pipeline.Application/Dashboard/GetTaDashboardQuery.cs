using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Pipeline.Application.Abstractions;
using Recuro.Pipeline.Application.Applications;
using Recuro.Pipeline.Domain.Applications;

namespace Recuro.Pipeline.Application.Dashboard;

/// <summary>A dashboard tile (frontend <c>DashboardData.stats</c> item). <c>tone</c> is '' | 'g' | 'a' | 'r' | 't'.</summary>
public sealed record DashboardStatDto(string Label, string Value, string Trend, string Tone);

/// <summary>Frontend <c>DashboardData.tatBreaches</c> item.</summary>
public sealed record TatBreachRowDto(string ReqId, string Position, string Stage, string StageTone, string Escalation, string Link);

/// <summary>Frontend <c>DashboardData.pipeline</c> item.</summary>
public sealed record PipelineStageCountDto(string Stage, int Count, string Color);

/// <summary>
/// Pipeline's part of the HR-TA dashboard (S-01, RCU-DSH-001/002), the gateway BFF's
/// <c>DashboardFragment</c>: the funnel, the stage TAT breaches and their tile. Other lists belong to
/// other services and are left out.
/// </summary>
public sealed record TaDashboardFragmentDto(
    IReadOnlyList<DashboardStatDto> Stats,
    IReadOnlyList<TatBreachRowDto> TatBreaches,
    IReadOnlyList<PipelineStageCountDto> Pipeline);

public sealed record GetTaDashboardQuery(int MaxBreaches = 10) : IQuery<TaDashboardFragmentDto>;

/// <summary>Display text for the fragment, kept in one place (the dashboard renders it as is).</summary>
internal static class DashboardText
{
    public const string BreachesTile = "TAT Breaches";
    public const string EscalationsActive = "Escalations active";
    public const string NoBreaches = "All stages within TAT";
    public const string BoardLink = "/pipeline";

    /// <summary>Funnel bar colours, the design tokens the frontend mock uses.</summary>
    public static readonly IReadOnlyDictionary<ApplicationStage, string> StageColors = new Dictionary<ApplicationStage, string>
    {
        [ApplicationStage.Sourced] = "var(--slate)",
        [ApplicationStage.Screened] = "var(--navy-lt)",
        [ApplicationStage.Interview] = "var(--amber)",
        [ApplicationStage.Selection] = "var(--purple)",
        [ApplicationStage.BGV] = "var(--teal)",
        [ApplicationStage.Offer] = "var(--green)",
    };

    public static readonly IReadOnlyDictionary<ApplicationStage, string> StageLabels = new Dictionary<ApplicationStage, string>
    {
        [ApplicationStage.Sourced] = "Sourcing",
        [ApplicationStage.Screened] = "Screening",
        [ApplicationStage.Interview] = "Interview",
        [ApplicationStage.Selection] = "Selection",
        [ApplicationStage.BGV] = "BGV",
        [ApplicationStage.Offer] = "Offer",
    };

    public static readonly IReadOnlyDictionary<string, string> RoleLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["hrta"] = "HR-TA",
        ["hrhead"] = "HR Head",
        ["mdceo"] = "MD/CEO",
        ["hod"] = "HOD",
    };
}

internal sealed class GetTaDashboardQueryHandler(IApplicationRepository applications, ITatRules tatRules, TimeProvider clock)
    : IQueryHandler<GetTaDashboardQuery, TaDashboardFragmentDto>
{
    public async Task<Result<TaDashboardFragmentDto>> Handle(GetTaDashboardQuery query, CancellationToken ct)
    {
        var counts = await applications.CountByStageAsync(ct);
        var funnel = ApplicationTransitions.BoardColumns
            .Select(stage => new PipelineStageCountDto(stage.ToString(), counts.GetValueOrDefault(stage), DashboardText.StageColors[stage]))
            .ToList();

        var rules = await tatRules.GetAsync(ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var breached = await applications.ListTatBreachedAsync(query.MaxBreaches, ct);
        var rows = breached.Select(a => BreachRow(a, rules.GetValueOrDefault(a.Stage), today)).ToList();

        var tile = new DashboardStatDto(
            DashboardText.BreachesTile,
            rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            rows.Count > 0 ? DashboardText.EscalationsActive : DashboardText.NoBreaches,
            rows.Count > 0 ? "r" : "g");
        return new TaDashboardFragmentDto([tile], rows, funnel);
    }

    private static TatBreachRowDto BreachRow(Domain.Applications.Application application, StageTatRule? rule, DateOnly today)
    {
        var elapsed = WorkingDaysBetween(DateOnly.FromDateTime(application.StageEnteredAt.UtcDateTime), today);
        var label = DashboardText.StageLabels.GetValueOrDefault(application.Stage, application.Stage.ToString());
        var stage = rule is { WorkingDays: > 0 } ? $"{label} {elapsed}d / {rule.WorkingDays}d TAT" : $"{label} {elapsed}d";

        // One day over is a warning; more is a breach that needs the escalation to act.
        var tone = rule is { WorkingDays: > 0 } && elapsed - rule.WorkingDays <= 1 ? "amber" : "red";
        var escalation = rule is null || string.IsNullOrEmpty(rule.Escalation)
            ? string.Empty
            : DashboardText.RoleLabels.GetValueOrDefault(rule.Escalation, rule.Escalation);

        // No candidate data here: Pipeline holds none, and the dashboard row names the application only.
        return new TatBreachRowDto(application.ReqId, application.AppId, stage, tone, escalation, DashboardText.BoardLink);
    }

    /// <summary>Working days from <paramref name="from"/> to <paramref name="to"/>, weekends excluded (display only).</summary>
    private static int WorkingDaysBetween(DateOnly from, DateOnly to)
    {
        var days = 0;
        for (var day = from.AddDays(1); day <= to; day = day.AddDays(1))
        {
            if (WorkingDays.IsWorkingDay(day))
            {
                days++;
            }
        }

        return days;
    }
}
