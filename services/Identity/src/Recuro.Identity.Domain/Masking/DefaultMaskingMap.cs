namespace Recuro.Identity.Domain.Masking;

/// <summary>
/// The default masking map (FRD §3.2 "🔒 masked", RCU-PLT-003, §5.7). Field names are the frontend
/// DTO fields (<c>frontend/src/domain/types.ts</c>). HR-TA and HR Head see everything.
/// </summary>
public static class DefaultMaskingMap
{
    public const string Version = "masking-2026.10";

    public static MaskingMap Create()
    {
        // People outside HR never see a candidate's personal data.
        var noAccess = new Dictionary<string, MaskStrategy>
        {
            ["name"] = MaskStrategy.Hide,
            ["email"] = MaskStrategy.Hide,
            ["phone"] = MaskStrategy.Hide,
            ["summary"] = MaskStrategy.Hide,
            ["currentCtc"] = MaskStrategy.Hide,
            ["expectedCtc"] = MaskStrategy.Hide,
        };

        return new MaskingMap(Version, new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, MaskStrategy>>>
        {
            ["candidate"] = new Dictionary<string, IReadOnlyDictionary<string, MaskStrategy>>
            {
                // MD/CEO: CTC and contact PII masked (FRD §3.2, PLT-003).
                [PersonaRoles.MdCeo] = new Dictionary<string, MaskStrategy>
                {
                    ["currentCtc"] = MaskStrategy.Hide,
                    ["expectedCtc"] = MaskStrategy.Hide,
                    ["phone"] = MaskStrategy.Partial,
                    ["email"] = MaskStrategy.Partial,
                },
                [PersonaRoles.Employee] = noAccess,
                [PersonaRoles.Candidate] = noAccess,
            },
            ["approval"] = new Dictionary<string, IReadOnlyDictionary<string, MaskStrategy>>
            {
                // The inbox's CTC/band amount ("sensitive") follows the same rule as candidate CTC.
                [PersonaRoles.MdCeo] = new Dictionary<string, MaskStrategy> { ["sensitive"] = MaskStrategy.Hide },
                [PersonaRoles.Employee] = new Dictionary<string, MaskStrategy> { ["sensitive"] = MaskStrategy.Hide },
                [PersonaRoles.Candidate] = new Dictionary<string, MaskStrategy> { ["sensitive"] = MaskStrategy.Hide },
            },
        });
    }
}
