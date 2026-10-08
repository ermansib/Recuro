using System.Net.Http.Json;
using Recuro.Employee.Application.Abstractions;

namespace Recuro.Employee.Infrastructure.Clients;

/// <summary>
/// The Config service's business calendar (<c>GET /api/v1/resolve/working-days</c>), called with the HR
/// user's own token. The only place Employee counts working days; failures are 503, never a guessed date.
/// </summary>
internal sealed class ConfigCalendarClient(HttpClient http) : IWorkingDayCalendar
{
    public async Task<DateOnly> AddAsync(DateOnly from, int days, CancellationToken ct)
    {
        var uri = new Uri(FormattableString.Invariant($"api/v1/resolve/working-days?from={from:yyyy-MM-dd}&days={days}"), UriKind.Relative);
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(uri, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException("The Config service is unreachable.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new DependencyUnavailableException(FormattableString.Invariant($"Config answered {(int)response.StatusCode} for working days."));
            }

            var body = await response.Content.ReadFromJsonAsync<WorkingDaysResponse>(ct);
            return body?.Date ?? throw new DependencyUnavailableException("Config returned no date.");
        }
    }

    private sealed record WorkingDaysResponse(DateOnly? Date);
}
