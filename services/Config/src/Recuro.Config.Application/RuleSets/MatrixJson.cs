using System.Text.Json;
using System.Text.Json.Serialization;
using Recuro.Config.Domain.Rules;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.RuleSets;

/// <summary>
/// Reads and writes matrix content. Strict on input: unknown properties, missing required values and
/// nulls where the type has none are errors, so a typo never becomes a silently ignored rule.
/// </summary>
public static class MatrixJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static Type ClrType(MatrixType type) => type switch
    {
        MatrixType.Doa => typeof(DoaMatrix),
        MatrixType.Tat => typeof(TatMatrix),
        MatrixType.Offer => typeof(OfferMatrix),
        MatrixType.Escalation => typeof(EscalationMatrix),
        MatrixType.Bgv => typeof(BgvMatrix),
        MatrixType.Calendar => typeof(CalendarMatrix),
        MatrixType.Interview => typeof(InterviewMatrix),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>Parses <paramref name="content"/> as a <paramref name="type"/> matrix, or explains why it can't.</summary>
    public static bool TryParse(MatrixType type, JsonElement content, out object? matrix, out string? error)
    {
        matrix = null;
        error = null;
        if (content.ValueKind != JsonValueKind.Object)
        {
            error = "content must be a JSON object.";
            return false;
        }

        try
        {
            matrix = content.Deserialize(ClrType(type), Options);
            if (matrix is null)
            {
                error = "content is empty.";
            }
        }
        catch (JsonException ex)
        {
            error = $"content is not a valid {type.ToKey()} matrix: {ex.Message}";
        }

        return matrix is not null;
    }

    public static T Read<T>(string content) =>
        JsonSerializer.Deserialize<T>(content, Options) ?? throw new JsonException("Stored matrix content is empty.");

    public static string Write(object matrix) => JsonSerializer.Serialize(matrix, matrix.GetType(), Options);

    public static JsonElement ToElement(string content)
    {
        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }
}
