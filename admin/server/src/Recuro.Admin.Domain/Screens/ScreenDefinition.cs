using Recuro.Admin.Domain.Common;

namespace Recuro.Admin.Domain.Screens;

/// <summary>
/// A screen of the main Recuro portal and its configurable fields, as shipped by the product.
/// Platform-owned: tenants never change it, they layer a <see cref="TenantScreenConfiguration"/> on top.
/// </summary>
public sealed class ScreenDefinition : Entity
{
    private readonly List<FieldDefinition> _fields = [];

    private ScreenDefinition()
    {
    }

    /// <summary>Stable key the main app looks screens up by, for example "mrf".</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>Prototype screen id, for example "S-02".</summary>
    public string Code { get; private set; } = string.Empty;

    public string Module { get; private set; } = string.Empty;

    public string DefaultTitle { get; private set; } = string.Empty;

    public string DefaultSubtitle { get; private set; } = string.Empty;

    /// <summary>False for screens the portal cannot work without, such as the dashboard.</summary>
    public bool CanDisable { get; private set; }

    public int SortOrder { get; private set; }

    public IReadOnlyList<FieldDefinition> Fields => _fields;

    public static ScreenDefinition Create(
        string key,
        string code,
        string module,
        string defaultTitle,
        string defaultSubtitle,
        bool canDisable,
        int sortOrder,
        IEnumerable<FieldDefinition> fields,
        Guid? id = null)
    {
        var screen = new ScreenDefinition
        {
            Id = id ?? Guid.NewGuid(),
            Key = key,
            Code = code,
            Module = module,
            DefaultTitle = defaultTitle,
            DefaultSubtitle = defaultSubtitle,
            CanDisable = canDisable,
            SortOrder = sortOrder,
        };
        screen._fields.AddRange(fields);
        return screen;
    }

    public FieldDefinition? FindField(string fieldKey) =>
        _fields.Find(field => string.Equals(field.Key, fieldKey, StringComparison.Ordinal));
}

/// <summary>A field on a screen with its product defaults.</summary>
/// <param name="IsLocked">Locked fields are needed by a rule or state machine and must stay visible and required.</param>
public sealed record FieldDefinition(
    string Key,
    string DefaultLabel,
    FieldDataType DataType,
    bool DefaultRequired,
    bool IsLocked,
    int SortOrder);

public enum FieldDataType
{
    Text,
    LongText,
    Number,
    Currency,
    Date,
    Select,
    Email,
    Phone,
    File,
}
