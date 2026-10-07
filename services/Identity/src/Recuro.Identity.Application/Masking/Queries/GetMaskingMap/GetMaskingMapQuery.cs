using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Identity.Application.Abstractions;
using Recuro.Identity.Domain;

namespace Recuro.Identity.Application.Masking.Queries.GetMaskingMap;

/// <summary>RCU-AUT-004: which fields <see cref="Role"/> sees masked on <see cref="Resource"/>.</summary>
public sealed record GetMaskingMapQuery(string Role, string Resource) : IQuery<MaskingMapDto>;

/// <summary>
/// <c>fields</c> maps a DTO field name to <c>hide</c>, <c>partial</c> or <c>hash</c>. Fields not listed are
/// returned unmasked. Services apply it when they serialise.
/// </summary>
public sealed record MaskingMapDto(string Resource, string Role, string Version, IReadOnlyDictionary<string, string> Fields);

internal sealed class GetMaskingMapQueryHandler(IMaskingMapProvider maps) : IQueryHandler<GetMaskingMapQuery, MaskingMapDto>
{
    public async Task<Result<MaskingMapDto>> Handle(GetMaskingMapQuery query, CancellationToken ct)
    {
        var map = await maps.GetAsync(ct);
        var fields = map.For(query.Role, query.Resource);
        if (fields is null)
        {
            return Error.NotFound(
                "masking_map_not_found",
                $"No masking map for role '{query.Role}' on '{query.Resource}'. Roles: {string.Join(", ", PersonaRoles.All)}, {PersonaRoles.Service}. Resources: {string.Join(", ", map.Resources)}.");
        }

        var wire = fields.ToDictionary(f => f.Key, f => f.Value.ToString().ToLowerInvariant(), StringComparer.Ordinal);
        return new MaskingMapDto(query.Resource, query.Role, map.Version, wire);
    }
}
