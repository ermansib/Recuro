using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.UnitTests;

public class ThemePresetTests
{
    private static readonly ThemePalette Light = new("#1E2A5E", "#22D3EE", "#7C3AED", "#F4F6FB", "#FFFFFF", "#111827");
    private static readonly ThemePalette Dark = new("#A5B4FC", "#22D3EE", "#A78BFA", "#0B1020", "#151C36", "#E5E7EB");

    internal static ThemePreset ValidPreset() => ThemePreset.Create("Test-Theme", "Test", Light, Dark, 1).Value;

    [Fact]
    public void Contrast_ratio_matches_wcag_reference_values()
    {
        Assert.Equal(21, HexColor.ContrastRatio("#000000", "#FFFFFF"), 2);
        Assert.Equal(1, HexColor.ContrastRatio("#777777", "#777777"), 2);
        Assert.Equal(4.48, HexColor.ContrastRatio("#777777", "#FFFFFF"), 2);
    }

    [Fact]
    public void Create_rejects_colours_that_are_not_hex()
    {
        var result = ThemePreset.Create("x", "X", Light with { Accent = "purple" }, Dark, 1);

        Assert.Equal("theme.colour", result.Error!.Code);
    }

    [Fact]
    public void Create_normalises_key_and_starts_unpublished()
    {
        var preset = ValidPreset();

        Assert.Equal("test-theme", preset.Key);
        Assert.False(preset.IsPublished);
    }

    [Fact]
    public void Publish_refuses_text_below_aa_contrast()
    {
        var preset = ThemePreset.Create("x", "X", Light with { Text = "#9CA3AF" }, Dark, 1).Value;

        Assert.Equal("theme.textContrast", preset.Publish().Error!.Code);
        Assert.False(preset.IsPublished);
    }

    [Fact]
    public void Publish_refuses_a_primary_that_disappears_on_the_surface()
    {
        var preset = ThemePreset.Create("x", "X", Light, Dark with { Primary = "#1A2040" }, 1).Value;

        Assert.Equal("theme.primaryContrast", preset.Publish().Error!.Code);
    }

    [Fact]
    public void PaletteFor_picks_dark_only_in_dark_mode()
    {
        var preset = ValidPreset();

        Assert.Same(preset.Dark, preset.PaletteFor(ThemeMode.Dark));
        Assert.Same(preset.Light, preset.PaletteFor(ThemeMode.Light));
        Assert.Same(preset.Light, preset.PaletteFor(ThemeMode.System));
    }
}
