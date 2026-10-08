using System.Globalization;
using System.Text.Json.Serialization;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Application.Dashboard;

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
    public const string OffersPendingLabel = "Offers Pending";
    public const string Tone = "t";
}

internal sealed class GetTaDashboardQueryHandler(IOfferRepository offers) : IQueryHandler<GetTaDashboardQuery, DashboardFragmentDto>
{
    public async Task<Result<DashboardFragmentDto>> Handle(GetTaDashboardQuery query, CancellationToken ct)
    {
        var pending = await offers.CountAsync([OfferState.PendingApproval, OfferState.Approved], ct);
        var sent = await offers.CountAsync([OfferState.Sent], ct);
        var trend = string.Create(CultureInfo.InvariantCulture, $"{pending} awaiting approval or release · {sent} with candidates");
        return new DashboardFragmentDto(
            [new DashboardStatDto(GetTaDashboardQuery.OffersPendingLabel, (pending + sent).ToString(CultureInfo.InvariantCulture), trend, GetTaDashboardQuery.Tone)]);
    }
}
