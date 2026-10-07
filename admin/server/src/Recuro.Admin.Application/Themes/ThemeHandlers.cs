using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.Application.Themes;

public sealed record ThemePaletteDto(string Primary, string Secondary, string Accent, string Background, string Surface, string Text)
{
    public static ThemePaletteDto From(ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return new(palette.Primary, palette.Secondary, palette.Accent, palette.Background, palette.Surface, palette.Text);
    }

    public ThemePalette ToDomain() => new(Primary, Secondary, Accent, Background, Surface, Text);
}

public sealed record ThemePresetDto(string Key, string Name, ThemePaletteDto Light, ThemePaletteDto Dark, bool IsPublished)
{
    public static ThemePresetDto From(ThemePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return new(preset.Key, preset.Name, ThemePaletteDto.From(preset.Light), ThemePaletteDto.From(preset.Dark), preset.IsPublished);
    }
}

public sealed class ListThemePresetsHandler(IThemePresetRepository presets)
{
    public async Task<IReadOnlyList<ThemePresetDto>> HandleAsync(bool publishedOnly, CancellationToken ct) =>
        (await presets.ListAsync(publishedOnly, ct)).Select(ThemePresetDto.From).ToList();
}

public sealed record CreateThemePresetRequest(string Key, string Name, ThemePaletteDto Light, ThemePaletteDto Dark, bool Publish);

public sealed class CreateThemePresetHandler(IThemePresetRepository presets, IUnitOfWork unitOfWork)
{
    public async Task<Result<ThemePresetDto>> HandleAsync(CreateThemePresetRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Light);
        ArgumentNullException.ThrowIfNull(request.Dark);

        var existing = await presets.ListAsync(publishedOnly: false, ct);
        var created = ThemePreset.Create(request.Key, request.Name, request.Light.ToDomain(), request.Dark.ToDomain(), existing.Count + 1);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var preset = created.Value;
        if (await presets.KeyExistsAsync(preset.Key, ct))
        {
            return ThemeErrors.KeyTaken;
        }

        if (request.Publish)
        {
            var published = preset.Publish();
            if (published.IsFailure)
            {
                return published.Error!;
            }
        }

        presets.Add(preset);
        await unitOfWork.SaveChangesAsync(ct);
        return ThemePresetDto.From(preset);
    }
}

public sealed class SetThemePublishedHandler(IThemePresetRepository presets, IUnitOfWork unitOfWork)
{
    public async Task<Result<ThemePresetDto>> HandleAsync(string key, bool publish, CancellationToken ct)
    {
        var preset = await presets.GetByKeyAsync(key, ct);
        if (preset is null)
        {
            return ThemeErrors.NotFound(key);
        }

        if (publish)
        {
            var result = preset.Publish();
            if (result.IsFailure)
            {
                return result.Error!;
            }
        }
        else
        {
            preset.Unpublish();
        }

        await unitOfWork.SaveChangesAsync(ct);
        return ThemePresetDto.From(preset);
    }
}
