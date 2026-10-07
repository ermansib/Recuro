namespace Recuro.Identity.Domain.Masking;

/// <summary>How a service masks a field when it serialises a response (RCU-AUT-004).</summary>
public enum MaskStrategy
{
    /// <summary>Leave the field out (or null).</summary>
    Hide,

    /// <summary>Keep the last characters only, e.g. <c>••••••3210</c>.</summary>
    Partial,

    /// <summary>Replace with a stable one-way hash, so equal values still group together.</summary>
    Hash,
}

/// <summary>
/// Field → strategy per resource and role, versioned as configuration (RCU-AUT-004, RCU-PLT-003).
/// A field that is not listed is returned as is.
/// </summary>
public sealed class MaskingMap
{
    private readonly Dictionary<string, Dictionary<string, IReadOnlyDictionary<string, MaskStrategy>>> _byResource;

    public MaskingMap(string version, IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, MaskStrategy>>> byResource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(byResource);
        Version = version;
        _byResource = byResource.ToDictionary(
            r => r.Key,
            r => r.Value.ToDictionary(role => role.Key, role => role.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    public string Version { get; }

    public IEnumerable<string> Resources => _byResource.Keys;

    /// <summary>
    /// The fields <paramref name="role"/> sees masked on <paramref name="resource"/>. Null when the
    /// resource or role is unknown; an empty map when the role sees everything.
    /// </summary>
    public IReadOnlyDictionary<string, MaskStrategy>? For(string role, string resource)
    {
        if (!PersonaRoles.HasMaskingMap(role) || !_byResource.TryGetValue(resource, out var roles))
        {
            return null;
        }

        return roles.TryGetValue(role, out var fields) ? fields : new Dictionary<string, MaskStrategy>();
    }
}
