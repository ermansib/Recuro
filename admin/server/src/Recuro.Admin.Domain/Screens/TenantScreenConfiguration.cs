using Recuro.Admin.Domain.Common;

namespace Recuro.Admin.Domain.Screens;

/// <summary>A tenant's overrides for one screen: on/off, header text, and per-field label, visibility, required and order.</summary>
public sealed class TenantScreenConfiguration : Entity, ITenantOwned
{
    public const int TextMaxLength = 80;

    private readonly List<TenantFieldSetting> _fields = [];

    private TenantScreenConfiguration()
    {
    }

    public Guid TenantId { get; private set; }

    public string ScreenKey { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; } = true;

    /// <summary>Header title override; null means the product default.</summary>
    public string? Title { get; private set; }

    /// <summary>Header subtitle override; null means the product default.</summary>
    public string? Subtitle { get; private set; }

    public IReadOnlyList<TenantFieldSetting> Fields => _fields;

    public static TenantScreenConfiguration CreateFor(Guid tenantId, string screenKey) =>
        new() { Id = Guid.NewGuid(), TenantId = tenantId, ScreenKey = screenKey };

    /// <summary>Replaces the overrides after checking them against the product definition.</summary>
    public Result Update(ScreenDefinition definition, ScreenChanges changes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(changes);

        if (!changes.IsEnabled && !definition.CanDisable)
        {
            return ScreenErrors.CannotDisable(definition.Key);
        }

        if (IsTooLong(changes.Title) || IsTooLong(changes.Subtitle))
        {
            return ScreenErrors.TextTooLong;
        }

        var duplicate = changes.Fields.GroupBy(f => f.FieldKey, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            return ScreenErrors.DuplicateField(duplicate.Key);
        }

        foreach (var field in changes.Fields)
        {
            var error = ValidateField(definition, field);
            if (error is not null)
            {
                return error;
            }
        }

        IsEnabled = changes.IsEnabled;
        Title = NullIfBlank(changes.Title);
        Subtitle = NullIfBlank(changes.Subtitle);
        _fields.Clear();
        _fields.AddRange(changes.Fields.Select(f => f with { Label = NullIfBlank(f.Label) }));
        return Result.Success();
    }

    private static Error? ValidateField(ScreenDefinition definition, TenantFieldSetting field)
    {
        var fieldDefinition = definition.FindField(field.FieldKey);
        if (fieldDefinition is null)
        {
            return ScreenErrors.UnknownField(definition.Key, field.FieldKey);
        }

        if (IsTooLong(field.Label))
        {
            return ScreenErrors.TextTooLong;
        }

        if (fieldDefinition.IsLocked && (!field.IsVisible || !field.IsRequired))
        {
            return ScreenErrors.LockedField(field.FieldKey);
        }

        return field.IsRequired && !field.IsVisible ? ScreenErrors.RequiredMustBeVisible(field.FieldKey) : null;
    }

    private static bool IsTooLong(string? text) => text is not null && text.Trim().Length > TextMaxLength;

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>A tenant's override for one field. A null label means the product default.</summary>
public sealed record TenantFieldSetting(string FieldKey, string? Label, bool IsVisible, bool IsRequired, int SortOrder);

public sealed record ScreenChanges(bool IsEnabled, string? Title, string? Subtitle, IReadOnlyList<TenantFieldSetting> Fields);

public static class ScreenErrors
{
    public static readonly Error TextTooLong =
        Error.Validation("screen.textTooLong", $"Titles and labels must be at most {TenantScreenConfiguration.TextMaxLength} characters.");

    public static Error NotFound(string key) => Error.NotFound("screen.notFound", $"Screen '{key}' was not found.");

    public static Error CannotDisable(string key) =>
        Error.Validation("screen.cannotDisable", $"Screen '{key}' is required by the portal and cannot be turned off.");

    public static Error UnknownField(string screen, string field) =>
        Error.Validation("screen.unknownField", $"Screen '{screen}' has no field '{field}'.");

    public static Error DuplicateField(string field) =>
        Error.Validation("screen.duplicateField", $"Field '{field}' appears more than once.");

    public static Error LockedField(string field) =>
        Error.Validation("screen.lockedField", $"Field '{field}' is used by a workflow rule, so it must stay visible and required.");

    public static Error RequiredMustBeVisible(string field) =>
        Error.Validation("screen.requiredHidden", $"Field '{field}' cannot be required while it is hidden.");
}
