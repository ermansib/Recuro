using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Recuro.Requisition.Application.Abstractions;

namespace Recuro.Requisition.Infrastructure.Clients;

/// <summary>
/// HTTP client for the Config service's resolution API (architecture.md, "Synchronous contracts").
/// Failures surface as <see cref="DependencyUnavailableException"/> (503), never as a guessed rule.
/// </summary>
public sealed class ConfigRulesClient(HttpClient http) : IRulesClient
{
    public async Task<DoaResolution?> ResolveDoaAsync(string grade, bool outOfBudget, DateTimeOffset at, CancellationToken ct)
    {
        var uri = string.Create(
            CultureInfo.InvariantCulture,
            $"api/v1/resolve/doa?grade={Uri.EscapeDataString(grade)}&budget={(outOfBudget ? "oob" : "in")}&at={Uri.EscapeDataString(at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}");
        using var response = await SendAsync(uri, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<DoaResolution>(ct)
            ?? throw new DependencyUnavailableException("Config returned an empty DOA resolution.");
    }

    public async Task<DateOnly> AddWorkingDaysAsync(DateOnly from, int days, string? location, string? versionId, CancellationToken ct)
    {
        var uri = string.Create(
            CultureInfo.InvariantCulture,
            $"api/v1/resolve/working-days?from={from:yyyy-MM-dd}&days={days}");
        if (!string.IsNullOrWhiteSpace(location))
        {
            uri += $"&location={Uri.EscapeDataString(location)}";
        }

        if (!string.IsNullOrWhiteSpace(versionId))
        {
            uri += $"&versionId={Uri.EscapeDataString(versionId)}";
        }

        using var response = await SendAsync(uri, ct);
        await EnsureSuccessAsync(response);
        var body = await response.Content.ReadFromJsonAsync<WorkingDaysResponse>(ct);
        return body?.Date ?? throw new DependencyUnavailableException("Config returned no date.");
    }

    private async Task<HttpResponseMessage> SendAsync(string uri, CancellationToken ct)
    {
        try
        {
            return await http.GetAsync(new Uri(uri, UriKind.Relative), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException("The Config service is unreachable.", ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync();
            throw new DependencyUnavailableException(
                string.Create(CultureInfo.InvariantCulture, $"Config answered {(int)response.StatusCode}: {Truncate(detail)}"));
        }
    }

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300];

    private sealed record WorkingDaysResponse(DateOnly Date, string? ConfigVersionId);
}
