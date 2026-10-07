using System.Globalization;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Config.Application.RuleSets;
using Recuro.Config.Domain.Rules;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.Resolve;

/// <summary>
/// The rules part of the frontend's <c>RuleConfig</c> (<c>getRules()</c>): version, effectiveFrom, doa,
/// offerMatrix and bgvChecks, field for field. Pick lists (departments, locations…) are tenant fields
/// owned by the admin portal; the gateway BFF composes the two.
/// </summary>
public sealed record GetRulesQuery(DateTimeOffset? At) : IQuery<RulesDto>;

/// <summary>The frontend's <c>DoaRoute</c>.</summary>
public sealed record DoaRouteDto(string Grade, string Initiating, string Recommending, string Approving, string ApproverRole, string BandLabel, TatRange OverallTat);

public sealed record RulesDto(
    string Version,
    string EffectiveFrom,
    IReadOnlyList<DoaRouteDto> Doa,
    IReadOnlyList<OfferMatrixRule> OfferMatrix,
    IReadOnlyList<BgvCheckRule> BgvChecks);

internal sealed class GetRulesQueryHandler(RuleSetResolver resolver) : IQueryHandler<GetRulesQuery, RulesDto>
{
    public async Task<Result<RulesDto>> Handle(GetRulesQuery query, CancellationToken ct)
    {
        var doa = await resolver.ResolveAsync(MatrixType.Doa, query.At, null, ct);
        var offer = await resolver.ResolveAsync(MatrixType.Offer, query.At, null, ct);
        var bgv = await resolver.ResolveAsync(MatrixType.Bgv, query.At, null, ct);
        var failure = new[] { doa, offer, bgv }.FirstOrDefault(r => r.IsFailure);
        if (failure is not null)
        {
            return failure.Error!;
        }

        var versions = new[] { doa.Value, offer.Value, bgv.Value };
        var routes = MatrixJson.Read<DoaMatrix>(doa.Value.Content).Routes
            .Select(r => new DoaRouteDto(r.Grade, r.Initiating, r.Recommending, r.Approving, r.ApproverRole, r.BandLabel, r.OverallTat))
            .ToList();

        return new RulesDto(
            string.Join(" · ", versions.Select(v => $"{v.MatrixType.ToKey()} v{v.Number}")),
            versions.Max(v => v.EffectiveFrom).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            routes,
            MatrixJson.Read<OfferMatrix>(offer.Value.Content).Rules,
            MatrixJson.Read<BgvMatrix>(bgv.Value.Content).Checks);
    }
}
