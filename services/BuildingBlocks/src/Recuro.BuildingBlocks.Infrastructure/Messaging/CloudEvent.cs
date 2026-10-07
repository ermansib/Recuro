using System.Text.Json;
using System.Text.Json.Serialization;
using Recuro.BuildingBlocks.Application.IntegrationEvents;

namespace Recuro.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// CloudEvents 1.0 structured JSON envelope (RCU-PLT-003) with Recuro extensions for tenant,
/// correlation, trace and actor. Every event on the bus has this shape.
/// </summary>
public sealed record CloudEvent
{
    public const string ContentType = "application/cloudevents+json";

    [JsonPropertyName("specversion")]
    public string SpecVersion { get; init; } = "1.0";

    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("time")]
    public required DateTimeOffset Time { get; init; }

    [JsonPropertyName("datacontenttype")]
    public string DataContentType { get; init; } = "application/json";

    /// <summary>Relative path of the JSON schema under <c>services/contracts/events</c>.</summary>
    [JsonPropertyName("dataschema")]
    public string? DataSchema { get; init; }

    [JsonPropertyName("tenantid")]
    public required Guid TenantId { get; init; }

    [JsonPropertyName("correlationid")]
    public string? CorrelationId { get; init; }

    /// <summary>W3C trace context of the publishing span, so traces continue across the bus.</summary>
    [JsonPropertyName("traceparent")]
    public string? TraceParent { get; init; }

    [JsonPropertyName("actorid")]
    public string? ActorId { get; init; }

    [JsonPropertyName("actorname")]
    public string? ActorName { get; init; }

    [JsonPropertyName("actorrole")]
    public string? ActorRole { get; init; }

    [JsonPropertyName("data")]
    public required JsonElement Data { get; init; }

    public EventMetadata ToMetadata() =>
        new(Id, Type, Source, Subject, Time, TenantId, CorrelationId, ActorId, ActorName, ActorRole);
}

/// <summary>One JSON setup for every payload on the bus: camelCase, like the HTTP DTOs.</summary>
public static class EventJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(CloudEvent cloudEvent) => JsonSerializer.Serialize(cloudEvent, Options);

    public static CloudEvent Deserialize(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize<CloudEvent>(utf8Json, Options)
        ?? throw new JsonException("Message body is not a CloudEvent.");
}
