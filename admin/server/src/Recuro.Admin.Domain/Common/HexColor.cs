using System.Globalization;
using System.Text.RegularExpressions;

namespace Recuro.Admin.Domain.Common;

/// <summary>Colour helpers for theme validation (WCAG 2.1 relative luminance and contrast ratio).</summary>
public static partial class HexColor
{
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexPattern();

    public static bool IsValid(string? value) => value is not null && HexPattern().IsMatch(value);

    /// <summary>WCAG contrast ratio between two #RRGGBB colours, from 1 to 21.</summary>
    public static double ContrastRatio(string first, string second)
    {
        var a = RelativeLuminance(first);
        var b = RelativeLuminance(second);
        var (lighter, darker) = a > b ? (a, b) : (b, a);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        if (!IsValid(hex))
        {
            throw new ArgumentException($"'{hex}' is not a #RRGGBB colour.", nameof(hex));
        }

        double Channel(int start)
        {
            var srgb = int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(1)) + (0.7152 * Channel(3)) + (0.0722 * Channel(5));
    }
}
