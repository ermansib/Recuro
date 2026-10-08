using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Recuro.Bgv.Application.Abstractions;
using Recuro.Bgv.Application.Cases;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.Bgv.Application.Dashboard;

/// <summary>
/// This service's part of the TA dashboard (RCU-DSH-001), merged by the gateway's <c>/bff/dashboard/ta</c>:
/// any subset of the frontend <c>DashboardData</c> lists, in the same shapes.
/// </summary>
public sealed record DashboardFragmentDto(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<DashboardStatDto>? Stats);

/// <summary>A dashboard tile. <c>tone</c> is '' | 'g' | 'a' | 'r' | 't'.</summary>
public sealed record DashboardStatDto(string Label, string Value, string Trend, string Tone);

/// <summary>The "BGV in Progress" tile: open and under-review cases, and how many are due soon.</summary>
public sealed record GetTaDashboardQuery : IQuery<DashboardFragmentDto>
{
    public const string Label = "BGV in Progress";
    public const string Tone = "a";
}

internal sealed class GetTaDashboardQueryHandler(IBgvCaseRepository cases, IOptions<BgvOptions> options, TimeProvider clock)
    : IQueryHandler<GetTaDashboardQuery, DashboardFragmentDto>
{
    public async Task<Result<DashboardFragmentDto>> Handle(GetTaDashboardQuery query, CancellationToken ct)
    {
        var window = options.Value.DueSoonWindow;
        var (inProgress, dueSoon) = await cases.CountInProgressAsync(clock.GetUtcNow() + window, ct);
        var trend = dueSoon > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{dueSoon} due within {window.TotalHours:0}h")
            : "None due soon";
        return new DashboardFragmentDto(
            [new DashboardStatDto(GetTaDashboardQuery.Label, inProgress.ToString(CultureInfo.InvariantCulture), trend, GetTaDashboardQuery.Tone)]);
    }
}
