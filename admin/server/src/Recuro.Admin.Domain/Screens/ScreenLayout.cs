namespace Recuro.Admin.Domain.Screens;

/// <summary>Merges product defaults with a tenant's overrides into what the main portal renders.</summary>
public static class ScreenLayout
{
    public static EffectiveScreen Resolve(ScreenDefinition definition, TenantScreenConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var fields = definition.Fields
            .Select(field =>
            {
                var setting = configuration?.Fields.FirstOrDefault(s => s.FieldKey == field.Key);
                return new EffectiveField(
                    field.Key,
                    setting?.Label ?? field.DefaultLabel,
                    field.DefaultLabel,
                    field.DataType,
                    setting?.IsVisible ?? true,
                    setting?.IsRequired ?? field.DefaultRequired,
                    field.IsLocked,
                    setting?.SortOrder ?? field.SortOrder,
                    field.DefaultRequired,
                    field.SortOrder);
            })
            .OrderBy(field => field.SortOrder)
            .ThenBy(field => field.Key, StringComparer.Ordinal)
            .ToList();

        return new EffectiveScreen(
            definition.Key,
            definition.Code,
            definition.Module,
            configuration?.IsEnabled ?? true,
            definition.CanDisable,
            configuration?.Title ?? definition.DefaultTitle,
            configuration?.Subtitle ?? definition.DefaultSubtitle,
            definition.DefaultTitle,
            definition.DefaultSubtitle,
            fields);
    }
}

public sealed record EffectiveScreen(
    string Key,
    string Code,
    string Module,
    bool IsEnabled,
    bool CanDisable,
    string Title,
    string Subtitle,
    string DefaultTitle,
    string DefaultSubtitle,
    IReadOnlyList<EffectiveField> Fields);

public sealed record EffectiveField(
    string Key,
    string Label,
    string DefaultLabel,
    FieldDataType DataType,
    bool IsVisible,
    bool IsRequired,
    bool IsLocked,
    int SortOrder,
    bool DefaultRequired,
    int DefaultSortOrder);
