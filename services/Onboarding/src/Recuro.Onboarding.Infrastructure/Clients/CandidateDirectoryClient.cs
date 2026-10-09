using System.Net;
using System.Net.Http.Json;
using Recuro.Onboarding.Application.Abstractions;

namespace Recuro.Onboarding.Infrastructure.Clients;

internal sealed record CandidateNameResponse(string? Name);

/// <summary>
/// Candidate's <c>GET /api/v1/candidates/{id}</c>, for the employee's name on the confirmation letter.
/// The call carries the caller's own credentials, so the masking map applies to the real reader.
/// </summary>
internal sealed class CandidateDirectoryClient(HttpClient http) : ICandidateDirectory
{
    public async Task<string?> GetNameAsync(string candidateId, CancellationToken ct)
    {
        if (!Guid.TryParse(candidateId, out var id))
        {
            return null;
        }

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(new Uri($"api/v1/candidates/{id}", UriKind.Relative), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException("The Candidate service is unreachable.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DependencyUnavailableException($"Candidate answered {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadFromJsonAsync<CandidateNameResponse>(ct);
            return string.IsNullOrWhiteSpace(body?.Name) ? null : body.Name;
        }
    }
}
