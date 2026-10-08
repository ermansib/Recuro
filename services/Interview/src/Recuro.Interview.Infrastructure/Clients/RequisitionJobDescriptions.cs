using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Recuro.Interview.Application.Abstractions;

namespace Recuro.Interview.Infrastructure.Clients;

/// <summary>The fields of Requisition's <c>JobDescriptionDto</c> this service reads (tolerant reader).</summary>
internal sealed record JobDescriptionResponse(string ReqId, string Grade, IReadOnlyList<string>? Competencies, IReadOnlyList<string>? Assessments);

/// <summary>Requisition's <c>GET /api/v1/requisitions/{reqId}/job-description</c>, called with the caller's identity.</summary>
public sealed class RequisitionJobDescriptions(HttpClient http) : IJobDescriptions
{
    public async Task<JobDescriptionView?> GetAsync(string reqId, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(new Uri($"api/v1/requisitions/{Uri.EscapeDataString(reqId)}/job-description", UriKind.Relative), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException("The Requisition service is unreachable.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DependencyUnavailableException(
                    string.Create(CultureInfo.InvariantCulture, $"Requisition answered {(int)response.StatusCode} for the job description."));
            }

            var body = await response.Content.ReadFromJsonAsync<JobDescriptionResponse>(ct)
                ?? throw new DependencyUnavailableException("Requisition returned an empty job description.");
            return new JobDescriptionView(body.ReqId, body.Grade, body.Competencies ?? [], body.Assessments ?? []);
        }
    }
}
