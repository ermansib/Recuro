using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Recuro.Pipeline.Application.Abstractions;

namespace Recuro.Pipeline.Infrastructure.Integration;

/// <summary>Bound from <c>Services:Candidate</c>.</summary>
public sealed class CandidateServiceOptions
{
    public const string SectionName = "Services:Candidate";

    /// <summary>The Candidate service's base URL, e.g. <c>http://localhost:5107/</c>.</summary>
    public Uri BaseUrl { get; set; } = new("http://localhost:5107/");

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// HTTP client for the Candidate service's public contract (<c>GET /api/v1/candidates/{id}</c> and
/// <c>GET /api/v1/candidates?ids=</c>). The API layer forwards the caller's own credentials, so the
/// Candidate service authorises and masks for the real caller. One hop, never chained further.
/// </summary>
internal sealed class CandidateDirectoryClient(HttpClient http) : ICandidateDirectory
{
    private const int BatchSize = 200;

    public async Task<bool> ExistsAsync(string candidateId, CancellationToken ct)
    {
        if (!Guid.TryParse(candidateId, out var id))
        {
            return false;
        }

        using var response = await http.GetAsync(new Uri($"api/v1/candidates/{id}", UriKind.Relative), ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<IReadOnlyDictionary<string, JsonElement>> GetManyAsync(IReadOnlyCollection<string> candidateIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidateIds);
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var ids = candidateIds.Where(id => Guid.TryParse(id, out _)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var chunk in ids.Chunk(BatchSize))
        {
            var candidates = await http.GetFromJsonAsync<JsonElement>(
                new Uri($"api/v1/candidates?ids={string.Join(',', chunk)}", UriKind.Relative), ct);
            foreach (var candidate in candidates.EnumerateArray())
            {
                if (candidate.TryGetProperty("id", out var id) && id.GetString() is { } key)
                {
                    result[key] = candidate.Clone();
                }
            }
        }

        return result;
    }
}
