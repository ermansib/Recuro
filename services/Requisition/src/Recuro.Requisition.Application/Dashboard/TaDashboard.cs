using System.Globalization;
using System.Text.Json.Serialization;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Requisition.Application.Abstractions;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Application.Dashboard;

/// <summary>
/// This service's part of the TA dashboard (RCU-DSH-001), merged by the gateway's <c>/bff/dashboard/ta</c>:
/// any subset of the frontend <c>DashboardData</c> lists, in the same shapes.
/// </summary>
public sealed record DashboardFragmentDto(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<DashboardStatDto>? Stats);

/// <summary>A dashboard tile. <c>tone</c> is '' | 'g' | 'a' | 'r' | 't'.</summary>
public sealed record DashboardStatDto(string Label, string Value, string Trend, string Tone);

public sealed record GetTaDashboardQuery : IQuery<DashboardFragmentDto>
{
    public const string OpenMrfsLabel = "Open MRFs";

    /// <summary>Raised and not yet closed: from submission until filled, including holds.</summary>
    public static readonly IReadOnlyCollection<RequisitionState> OpenStates =
    [
        RequisitionState.PendingApproval,
        RequisitionState.Approved,
        RequisitionState.Sourcing,
        RequisitionState.Interviewing,
        RequisitionState.Selection,
        RequisitionState.BGV,
        RequisitionState.Offer,
        RequisitionState.OnHold,
    ];
}

internal sealed class GetTaDashboardQueryHandler(IRequisitionRepository requisitions, TimeProvider clock)
    : IQueryHandler<GetTaDashboardQuery, DashboardFragmentDto>
{
    public async Task<Result<DashboardFragmentDto>> Handle(GetTaDashboardQuery query, CancellationToken ct)
    {
        var (total, raised) = await requisitions.CountAsync(GetTaDashboardQuery.OpenStates, clock.GetUtcNow().AddDays(-7), ct);
        var trend = raised > 0 ? string.Create(CultureInfo.InvariantCulture, $"▲ +{raised} this week") : "No new MRFs this week";
        return new DashboardFragmentDto(
            [new DashboardStatDto(GetTaDashboardQuery.OpenMrfsLabel, total.ToString(CultureInfo.InvariantCulture), trend, string.Empty)]);
    }
}
