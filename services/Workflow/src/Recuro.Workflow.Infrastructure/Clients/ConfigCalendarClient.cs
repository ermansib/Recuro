using System.Globalization;
using System.Net.Http.Json;
using Recuro.Workflow.Application.Abstractions;

namespace Recuro.Workflow.Infrastructure.Clients;

/// <summary>
/// The tenant's business calendar from the Config service (<c>GET api/v1/resolve/working-days</c>,
/// architecture.md "Synchronous contracts"). Failures surface as <see cref="DependencyUnavailableException"/> (503).
/// </summary>
public sealed class ConfigCalendarClient(HttpClient http) : IBusinessCalendar
{
    public async Task<DateOnly> AddWorkingDaysAsync(DateOnly from, int days, string? versionId, CancellationToken ct)
    {
        var uri = string.Create(CultureInfo.InvariantCulture, $"api/v1/resolve/working-days?from={from:yyyy-MM-dd}&days={days}");
        if (!string.IsNullOrWhiteSpace(versionId))
        {
            uri += $"&versionId={Uri.EscapeDataString(versionId)}";
        }

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(new Uri(uri, UriKind.Relative), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException("The Config service is unreachable.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new DependencyUnavailableException(
                    string.Create(CultureInfo.InvariantCulture, $"Config answered {(int)response.StatusCode} for working days."));
            }

            var body = await response.Content.ReadFromJsonAsync<WorkingDaysResponse>(ct);
            return body?.Date ?? throw new DependencyUnavailableException("Config returned no date.");
        }
    }

    private sealed record WorkingDaysResponse(DateOnly Date, string? ConfigVersionId);
}
