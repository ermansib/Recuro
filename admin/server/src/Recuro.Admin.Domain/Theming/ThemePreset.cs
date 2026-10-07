using Recuro.Admin.Domain.Common;

namespace Recuro.Admin.Domain.Theming;

/// <summary>A named three-colour theme with light and dark palettes. The platform console owns the library.</summary>
public sealed class ThemePreset : Entity
{
    public const string DefaultKey = "recuro-classic";

    /// <summary>WCAG 2.1 AA minimum for body text (1.4.3).</summary>
    public const double TextContrastMinimum = 4.5;

    /// <summary>WCAG 2.1 AA minimum for UI components such as buttons (1.4.11).</summary>
    public const double ComponentContrastMinimum = 3.0;

    private ThemePreset()
    {
    }

    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public ThemePalette Light { get; private set; } = null!;

    public ThemePalette Dark { get; private set; } = null!;

    public bool IsPublished { get; private set; }

    public int SortOrder { get; private set; }

    public static Result<ThemePreset> Create(
        string key, string name, ThemePalette light, ThemePalette dark, int sortOrder, Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(dark);

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
        {
            return ThemeErrors.MissingName;
        }

        var paletteError = light.Validate("light") ?? dark.Validate("dark");
        if (paletteError is not null)
        {
            return paletteError;
        }

        return new ThemePreset
        {
            Id = id ?? Guid.NewGuid(),
            Key = key.Trim().ToLowerInvariant(),
            Name = name.Trim(),
            Light = light,
            Dark = dark,
            SortOrder = sortOrder,
        };
    }

    /// <summary>Makes the preset selectable by tenants, but only if both palettes pass WCAG AA.</summary>
    public Result Publish()
    {
        var error = Light.CheckContrast("light") ?? Dark.CheckContrast("dark");
        if (error is not null)
        {
            return error;
        }

        IsPublished = true;
        return Result.Success();
    }

    public void Unpublish() => IsPublished = false;

    public ThemePalette PaletteFor(ThemeMode mode) => mode == ThemeMode.Dark ? Dark : Light;
}

/// <summary>Light or dark colour set. Primary, secondary and accent are the three brand colours.</summary>
public sealed record ThemePalette(
    string Primary,
    string Secondary,
    string Accent,
    string Background,
    string Surface,
    string Text)
{
    internal Error? Validate(string mode)
    {
        string[] colours = [Primary, Secondary, Accent, Background, Surface, Text];
        return colours.All(HexColor.IsValid)
            ? null
            : Error.Validation("theme.colour", $"Every {mode} colour must be a #RRGGBB hex value.");
    }

    internal Error? CheckContrast(string mode)
    {
        if (HexColor.ContrastRatio(Text, Background) < ThemePreset.TextContrastMinimum ||
            HexColor.ContrastRatio(Text, Surface) < ThemePreset.TextContrastMinimum)
        {
            return Error.Validation("theme.textContrast", $"The {mode} text colour needs a contrast of at least 4.5:1 on the background and surface.");
        }

        if (HexColor.ContrastRatio(Primary, Surface) < ThemePreset.ComponentContrastMinimum)
        {
            return Error.Validation("theme.primaryContrast", $"The {mode} primary colour needs a contrast of at least 3:1 on the surface.");
        }

        return null;
    }
}

/// <summary>Default colour mode for a tenant's users. System follows the device setting.</summary>
public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public static class ThemeErrors
{
    public static readonly Error MissingName = Error.Validation("theme.name", "Theme key and name are required.");

    public static readonly Error KeyTaken = Error.Conflict("theme.keyTaken", "Another theme already uses this key.");

    public static Error NotFound(string key) => Error.NotFound("theme.notFound", $"Theme '{key}' was not found.");

    public static Error NotPublished(string key) =>
        Error.Conflict("theme.notPublished", $"Theme '{key}' is not published, so tenants cannot use it yet.");
}
