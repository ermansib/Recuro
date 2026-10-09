using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Reporting.Application.Abstractions;
using Recuro.Reporting.Domain.Kpis;

namespace Recuro.Reporting.Application.Reports;

/// <summary>
/// RCU-AUT-004 on reports. Reports hold aggregates only (no names, contacts or salaries), but channel
/// spend is commercial detail: FRD §3.2 gives HR-TA the "team view" and HR Head / MD/CEO the full
/// register. Maps come from Identity (<c>GET /api/v1/identity/masking/{role}/report</c>); a role without
/// a map fails closed.
/// </summary>
public static class ReportMasking
{
    public const string Resource = "report";

    /// <summary>The Cost-per-Hire metric row.</summary>
    public const string CostPerHire = "costPerHire";

    /// <summary>Spend and CPH per source channel in Source Mix.</summary>
    public const string SourceCost = "sourceCost";

    public static readonly IReadOnlyList<string> SensitiveFields = [CostPerHire, SourceCost];

    private const string Hidden = "hide";

    /// <summary>The proposed Identity map, used only while Identity can't be reached and nothing is cached.</summary>
    public static IReadOnlyDictionary<string, string>? LocalFallback(string role) => role switch
    {
        "hrhead" or "mdceo" or "service" => new Dictionary<string, string>(StringComparer.Ordinal),
        "hrta" => new Dictionary<string, string>(StringComparer.Ordinal) { [SourceCost] = Hidden },
        _ => null,
    };

    /// <summary>
    /// Per field, the least restrictive answer across the caller's roles: visible if any role's map leaves
    /// it unmasked. No maps at all hides every sensitive field.
    /// </summary>
    public static IReadOnlySet<string> HiddenFields(IEnumerable<IReadOnlyDictionary<string, string>?> roleMaps)
    {
        ArgumentNullException.ThrowIfNull(roleMaps);
        var maps = roleMaps.OfType<IReadOnlyDictionary<string, string>>().ToList();
        return maps.Count == 0
            ? SensitiveFields.ToHashSet(StringComparer.Ordinal)
            : SensitiveFields.Where(f => maps.All(m => m.ContainsKey(f))).ToHashSet(StringComparer.Ordinal);
    }

    public static KpiRegisterDto Apply(KpiRegisterDto register, IReadOnlySet<string> hidden)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(hidden);
        if (hidden.Count == 0)
        {
            return register;
        }

        var kpis = hidden.Contains(CostPerHire)
            ? register.Kpis.Where(k => k.Key != MetricKeys.CostPerHire).ToList()
            : register.Kpis;
        var mix = hidden.Contains(SourceCost) || hidden.Contains(CostPerHire)
            ? register.SourceMix.Select(s => s with { Cost = null, CostPerHire = null }).ToList()
            : register.SourceMix;
        return register with { Kpis = kpis, SourceMix = mix };
    }
}

/// <summary>Masks registers for the current caller, or for the role a pack is addressed to.</summary>
public interface IReportMask
{
    Task<Func<KpiRegisterDto, KpiRegisterDto>> ForCallerAsync(CancellationToken ct);

    Task<Func<KpiRegisterDto, KpiRegisterDto>> ForRoleAsync(string role, CancellationToken ct);
}

internal sealed class ReportMask(ICurrentUser caller, IMaskingMaps maps) : IReportMask
{
    public Task<Func<KpiRegisterDto, KpiRegisterDto>> ForCallerAsync(CancellationToken ct) => ForRolesAsync(caller.Roles, ct);

    public Task<Func<KpiRegisterDto, KpiRegisterDto>> ForRoleAsync(string role, CancellationToken ct) => ForRolesAsync([role], ct);

    private async Task<Func<KpiRegisterDto, KpiRegisterDto>> ForRolesAsync(IEnumerable<string> roles, CancellationToken ct)
    {
        var roleMaps = new List<IReadOnlyDictionary<string, string>?>();
        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            roleMaps.Add(await maps.GetAsync(role, ct));
        }

        var hidden = ReportMasking.HiddenFields(roleMaps);
        return register => ReportMasking.Apply(register, hidden);
    }
}
