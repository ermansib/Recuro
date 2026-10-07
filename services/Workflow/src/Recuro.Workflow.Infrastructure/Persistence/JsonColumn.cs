using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Recuro.Workflow.Infrastructure.Persistence;

/// <summary>Stores small value objects and lists as jsonb documents, compared by content.</summary>
internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static ValueConverter<T, string> Converter<T>()
        where T : class => new(
        value => JsonSerializer.Serialize(value, Options),
        json => JsonSerializer.Deserialize<T>(json, Options)!);

    /// <summary>Records with list members don't compare by content, so compare the serialized form.</summary>
    public static ValueComparer<T> Comparer<T>()
        where T : class => new(
        (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
        value => JsonSerializer.Serialize(value, Options).GetHashCode(StringComparison.Ordinal),
        value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!);
}
