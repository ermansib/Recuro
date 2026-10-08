using System.Globalization;
using System.Net.Http.Json;
using Recuro.Bgv.Application.Abstractions;

namespace Recuro.Bgv.Infrastructure.Integration;

public sealed class WorkflowServiceOptions
{
    public const string SectionName = "Services:Workflow";

    public Uri BaseUrl { get; set; } = new("http://localhost:5106/");

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>HTTP client for the Workflow service's instance API (RCU-WFL-001).</summary>
internal sealed class WorkflowHttpClient(HttpClient http) : IWorkflowClient
{
    public async Task<Guid> StartAsync(WorkflowStart start, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(start);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("api/v1/workflows", UriKind.Relative))
        {
            Content = JsonContent.Create(start),
        };
        request.Headers.Add("Idempotency-Key", start.CorrelationKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException("The Workflow service is unreachable.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                throw new DependencyUnavailableException(
                    string.Create(CultureInfo.InvariantCulture, $"Workflow answered {(int)response.StatusCode}: {(detail.Length <= 300 ? detail : detail[..300])}"));
            }

            var body = await response.Content.ReadFromJsonAsync<StartedResponse>(ct);
            return body?.Id ?? throw new DependencyUnavailableException("Workflow returned no instance id.");
        }
    }

    private sealed record StartedResponse(Guid Id);
}
