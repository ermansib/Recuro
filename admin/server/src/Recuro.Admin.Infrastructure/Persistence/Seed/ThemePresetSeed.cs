using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.Infrastructure.Persistence.Seed;

/// <summary>The six presets shipped with Recuro. Each has three brand colours and a light and dark palette.</summary>
internal static class ThemePresetSeed
{
    public static IEnumerable<ThemePreset> All()
    {
        yield return Preset(ThemePreset.DefaultKey, "Recuro Classic", 1,
            new("#1E2A5E", "#22D3EE", "#7C3AED", "#F4F6FB", "#FFFFFF", "#111827"),
            new("#A5B4FC", "#22D3EE", "#A78BFA", "#0B1020", "#151C36", "#E5E7EB"));
        yield return Preset("emerald-trust", "Emerald Trust", 2,
            new("#065F46", "#34D399", "#D97706", "#F3FAF7", "#FFFFFF", "#0F172A"),
            new("#6EE7B7", "#34D399", "#FBBF24", "#07140F", "#0F241C", "#E5E7EB"));
        yield return Preset("royal-plum", "Royal Plum", 3,
            new("#4C1D95", "#C4B5FD", "#DB2777", "#F7F5FC", "#FFFFFF", "#111827"),
            new("#C4B5FD", "#A78BFA", "#F472B6", "#120B1F", "#1E1433", "#EDE9FE"));
        yield return Preset("ocean-teal", "Ocean Teal", 4,
            new("#0F766E", "#5EEAD4", "#4F46E5", "#F2F9F9", "#FFFFFF", "#0F172A"),
            new("#5EEAD4", "#2DD4BF", "#818CF8", "#061514", "#0E2422", "#E2E8F0"));
        yield return Preset("sunset-coral", "Sunset Coral", 5,
            new("#9A3412", "#FDBA74", "#0369A1", "#FDF7F3", "#FFFFFF", "#1C1917"),
            new("#FDBA74", "#FB923C", "#7DD3FC", "#1A0D07", "#2A160C", "#F5F5F4"));
        yield return Preset("graphite-gold", "Graphite Gold", 6,
            new("#1F2937", "#9CA3AF", "#B45309", "#F5F5F4", "#FFFFFF", "#111827"),
            new("#E5E7EB", "#9CA3AF", "#FBBF24", "#0C0C0D", "#1A1A1D", "#F3F4F6"));
    }

    private static ThemePreset Preset(string key, string name, int order, ThemePalette light, ThemePalette dark)
    {
        var preset = ThemePreset.Create(key, name, light, dark, order).Value;
        var published = preset.Publish();
        return published.IsSuccess
            ? preset
            : throw new InvalidOperationException($"Seed theme '{key}' fails contrast: {published.Error!.Message}");
    }
}
