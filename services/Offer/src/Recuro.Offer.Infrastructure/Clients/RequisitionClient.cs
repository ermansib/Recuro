using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Recuro.Offer.Application.Abstractions;

namespace Recuro.Offer.Infrastructure.Clients;

/// <summary>The fields of Requisition's <c>RequisitionDto</c> this service reads (tolerant reader).</summary>
internal sealed record RequisitionResponse(string ReqId, string Designation, string Grade, string Location, string ReportingManager);

/// <summary>Requisition's <c>GET /api/v1/requisitions/{reqId}</c>, called with the caller's identity.</summary>
public sealed class RequisitionClient(HttpClient http) : IRequisitions
{
    public async Task<RequisitionView?> GetAsync(string reqId, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(new Uri($"api/v1/requisitions/{Uri.EscapeDataString(reqId)}", UriKind.Relative), ct);
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
                    string.Create(CultureInfo.InvariantCulture, $"Requisition answered {(int)response.StatusCode} for {reqId}."));
            }

            var body = await response.Content.ReadFromJsonAsync<RequisitionResponse>(ct)
                ?? throw new DependencyUnavailableException("Requisition returned an empty requisition.");
            return new RequisitionView(body.ReqId, body.Designation, body.Grade, body.Location, body.ReportingManager);
        }
    }
}
