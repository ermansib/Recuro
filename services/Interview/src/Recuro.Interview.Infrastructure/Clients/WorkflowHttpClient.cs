using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Recuro.Interview.Application.Abstractions;

namespace Recuro.Interview.Infrastructure.Clients;

/// <summary>HTTP client for the Workflow service's instance API (RCU-WFL-001).</summary>
public sealed class WorkflowHttpClient(HttpClient http) : IWorkflowClient
{
    public async Task<Guid> StartAsync(WorkflowStart start, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(start);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("api/v1/workflows", UriKind.Relative))
        {
            Content = JsonContent.Create(start),
        };
        request.Headers.Add("Idempotency-Key", start.CorrelationKey);

        using var response = await SendAsync(request, ct);
        await EnsureSuccessAsync(response);
        var body = await response.Content.ReadFromJsonAsync<StartedResponse>(ct);
        return body?.Id ?? throw new DependencyUnavailableException("Workflow returned no instance id.");
    }

    public async Task CancelAsync(Guid instanceId, string reason, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"api/v1/workflows/{instanceId}/cancel", UriKind.Relative))
        {
            Content = JsonContent.Create(new { reason }),
        };
        using var response = await SendAsync(request, ct);

        // Already finished or gone: there is nothing left to cancel.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            return;
        }

        await EnsureSuccessAsync(response);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException("The Workflow service is unreachable.", ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync();
            throw new DependencyUnavailableException(
                string.Create(CultureInfo.InvariantCulture, $"Workflow answered {(int)response.StatusCode}: {(detail.Length <= 300 ? detail : detail[..300])}"));
        }
    }

    private sealed record StartedResponse(Guid Id);
}
