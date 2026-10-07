using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Recuro.Gateway.Bff;

/// <summary>The dashboard and which sources could not answer.</summary>
internal sealed record DashboardResult(DashboardData Data, IReadOnlyList<string> Unavailable);

/// <summary>
/// RCU-GTW-002: one call for all dashboard tiles. Fans out to the configured services in parallel with
/// the caller's own token (each service scopes the numbers to the caller), and degrades per source: a
/// source that fails or times out contributes its tiles with "—" instead of failing the page.
/// </summary>
internal sealed partial class DashboardAggregator(
    IHttpClientFactory clients,
    IOptions<DashboardOptions> options,
    TimeProvider clock,
    ILogger<DashboardAggregator> logger)
{
    public const string ClientName = "bff";
    public const string Unavailable = "—";

    public async Task<DashboardResult> GetAsync(AuthenticationHeaderValue? caller, CancellationToken ct)
    {
        var settings = options.Value;
        var sources = settings.Sources.OrderBy(s => s.Value.Order).ThenBy(s => s.Key, StringComparer.Ordinal).ToList();
        var fragments = await Task.WhenAll(sources.Select(s => FetchAsync(s.Key, s.Value, caller, settings.SourceTimeout, ct)));

        var culture = CultureInfo.GetCultureInfo(settings.Culture);
        var today = clock.GetUtcNow();
        var data = new DashboardData(
            today.ToString("dddd, d MMM yyyy", culture),
            [.. fragments.SelectMany(f => f.Fragment.Stats ?? [])],
            [.. fragments.SelectMany(f => f.Fragment.TatBreaches ?? [])],
            [.. fragments.SelectMany(f => f.Fragment.Pipeline ?? [])],
            [.. fragments.SelectMany(f => f.Fragment.Kpis ?? [])],
            [.. fragments.SelectMany(f => f.Fragment.Upcoming ?? [])],
            today.ToString("MMM yyyy", culture));
        return new DashboardResult(data, fragments.Where(f => !f.Ok).Select(f => f.Name).ToList());
    }

    private async Task<(string Name, bool Ok, DashboardFragment Fragment)> FetchAsync(
        string name,
        DashboardSourceOptions source,
        AuthenticationHeaderValue? caller,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (source.Url is null)
        {
            return (name, false, Placeholder(source));
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
            request.Headers.Authorization = caller;
            using var response = await clients.CreateClient(ClientName).SendAsync(request, budget.Token);
            response.EnsureSuccessStatusCode();
            var fragment = await response.Content.ReadFromJsonAsync<DashboardFragment>(JsonSerializerOptions.Web, budget.Token);
            return (name, true, fragment ?? Placeholder(source));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            SourceUnavailable(logger, name, ex.GetType().Name);
            return (name, false, Placeholder(source));
        }
    }

    private static DashboardFragment Placeholder(DashboardSourceOptions source) =>
        new([.. source.Tiles.Select(t => new DashboardStat(t.Label, Unavailable, string.Empty, t.Tone))], null, null, null, null);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dashboard source {Source} unavailable ({Reason}); showing placeholders")]
    private static partial void SourceUnavailable(ILogger logger, string source, string reason);
}
