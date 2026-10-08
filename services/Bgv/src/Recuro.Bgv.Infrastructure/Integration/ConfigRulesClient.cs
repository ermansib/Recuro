using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.Bgv.Application.Abstractions;
using Recuro.Bgv.Domain.Cases;
using Recuro.BuildingBlocks.Application.Abstractions;

namespace Recuro.Bgv.Infrastructure.Integration;

public sealed class ConfigServiceOptions
{
    public const string SectionName = "Services:Config";

    public Uri BaseUrl { get; set; } = new("http://localhost:5102/");

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How long a resolved matrix or working-day answer is reused.</summary>
    public TimeSpan CacheFor { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the last good answer stays usable while Config can't be reached.</summary>
    public TimeSpan KeepStaleFor { get; set; } = TimeSpan.FromHours(24);

    /// <summary>After a failed call, the fallback is reused this long before Config is asked again.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>FRD §5.2 BGV completion TAT (top of 10–15 wd), used only while Config can't be reached.</summary>
    public int FallbackBgvTatWorkingDays { get; set; } = 15;
}

/// <summary>Config's <c>resolve/matrices/{type}</c> response.</summary>
internal sealed record MatrixResponse<T>(string ConfigVersionId, string MatrixType, int Number, DateTimeOffset EffectiveFrom, T Content);

internal sealed record BgvMatrixContent(IReadOnlyList<BgvRuleContent> Checks);

/// <summary>A BGV matrix row. <c>appliesWhen</c> is optional and read only when Config carries it.</summary>
internal sealed record BgvRuleContent(string Type, string Label, string Detail, string Condition, AppliesWhenContent? AppliesWhen);

internal sealed record AppliesWhenContent(bool? Always, IReadOnlyList<string>? Grades, IReadOnlyList<string>? AnyFlags);

internal sealed record TatMatrixContent(IReadOnlyList<TatStageContent> Stages);

internal sealed record TatStageContent(string Stage, int MaxWorkingDays);

internal sealed record EscalationMatrixContent(IReadOnlyList<EscalationIssueContent> Issues);

internal sealed record EscalationIssueContent(string Issue, RoleContactContent First, RoleContactContent Final);

internal sealed record RoleContactContent(string Role, string Label);

internal sealed record WorkingDaysResponse(DateOnly Date);

/// <summary>
/// The Config service's rules (architecture.md "Config: resolve rules"), cached per tenant. The BGV matrix
/// has no local copy: a case pins its version, so without Config (and nothing cached) initiation fails
/// with 503. The TAT, escalation route and calendar fall back to the FRD §5 defaults.
/// </summary>
internal sealed partial class ConfigRulesClient(
    HttpClient http,
    IMemoryCache cache,
    ITenantContext tenant,
    TimeProvider clock,
    IOptions<ConfigServiceOptions> options,
    ILogger<ConfigRulesClient> logger) : IBgvRules
{
    public const string BgvStage = "bgv";
    public const string AdverseIssue = "adverse-bgv";
    public const string LocalVersion = "local-default";

    public Task<CheckMatrix> GetCheckMatrixAsync(CancellationToken ct) =>
        ResolveAsync(
            "bgv-matrix",
            async () =>
            {
                var matrix = await GetMatrixAsync<BgvMatrixContent>("bgv", ct);
                return new CheckMatrix(matrix.ConfigVersionId, matrix.Content.Checks.Select(ToRule).ToList());
            },
            () => throw new DependencyUnavailableException("The Config service is unreachable and no BGV matrix is cached."),
            ct);

    public Task<int> GetTatWorkingDaysAsync(CancellationToken ct) =>
        ResolveAsync(
            "bgv-tat",
            async () =>
            {
                var matrix = await GetMatrixAsync<TatMatrixContent>("tat", ct);
                return matrix.Content.Stages.FirstOrDefault(s => s.Stage == BgvStage)?.MaxWorkingDays ?? options.Value.FallbackBgvTatWorkingDays;
            },
            () => options.Value.FallbackBgvTatWorkingDays,
            ct);

    public Task<EscalationRoute> GetAdverseEscalationAsync(CancellationToken ct) =>
        ResolveAsync(
            "adverse-escalation",
            async () =>
            {
                var matrix = await GetMatrixAsync<EscalationMatrixContent>("escalation", ct);
                var issue = matrix.Content.Issues.FirstOrDefault(i => i.Issue == AdverseIssue);
                return issue is null
                    ? LocalEscalation() with { ConfigVersionId = matrix.ConfigVersionId }
                    : new EscalationRoute(matrix.ConfigVersionId, new RoleRef(issue.First.Role, issue.First.Label), new RoleRef(issue.Final.Role, issue.Final.Label));
            },
            LocalEscalation,
            ct);

    public Task<DateOnly> AddWorkingDaysAsync(DateOnly from, int days, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(days);
        if (days == 0)
        {
            return Task.FromResult(from);
        }

        var query = $"api/v1/resolve/working-days?from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&days={days.ToString(CultureInfo.InvariantCulture)}";
        return ResolveAsync(
            $"working-days:{from.DayNumber}:{days}",
            async () => (await http.GetFromJsonAsync<WorkingDaysResponse>(new Uri(query, UriKind.Relative), ct)
                ?? throw new System.Text.Json.JsonException("Config returned an empty working-days answer.")).Date,
            () => AddWeekdays(from, days),
            ct);
    }

    internal static CheckRule ToRule(BgvRuleContent row) =>
        new(
            row.Type,
            row.Label,
            row.Detail,
            row.Condition,
            row.AppliesWhen is { } w ? new AppliesWhen(w.Always ?? false, w.Grades ?? [], w.AnyFlags ?? []) : null);

    private static EscalationRoute LocalEscalation() =>
        new(LocalVersion, new RoleRef("hrhead", "HR Head + Compliance"), new RoleRef("mdceo", "MD/CEO"));

    private static DateOnly AddWeekdays(DateOnly from, int days)
    {
        var day = from;
        for (var added = 0; added < days;)
        {
            day = day.AddDays(1);
            if (WorkingDays.IsWorkingDay(day))
            {
                added++;
            }
        }

        return day;
    }

    private async Task<MatrixResponse<T>> GetMatrixAsync<T>(string type, CancellationToken ct)
    {
        var at = Uri.EscapeDataString(clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        return await http.GetFromJsonAsync<MatrixResponse<T>>(new Uri($"api/v1/resolve/matrices/{type}?at={at}", UriKind.Relative), ct)
            ?? throw new System.Text.Json.JsonException($"Config returned an empty {type} matrix.");
    }

    private async Task<T> ResolveAsync<T>(string what, Func<Task<T>> fetch, Func<T> fallback, CancellationToken ct)
        where T : notnull
    {
        var settings = options.Value;
        var freshKey = $"bgv:config:{tenant.TenantId}:{what}";
        var lastGoodKey = $"{freshKey}:last-good";
        if (cache.TryGetValue(freshKey, out T? fresh) && fresh is not null)
        {
            return fresh;
        }

        try
        {
            var value = await fetch();
            cache.Set(freshKey, value, settings.CacheFor);
            cache.Set(lastGoodKey, value, settings.KeepStaleFor);
            return value;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            // The last good answer if there is one, otherwise the local rule; Config is asked again after RetryAfter.
            var hasLastGood = cache.TryGetValue(lastGoodKey, out T? lastGood) && lastGood is not null;
            ConfigUnavailable(logger, what, hasLastGood, ex);
            var value = hasLastGood ? lastGood! : fallback();
            cache.Set(freshKey, value, settings.RetryAfter);
            return value;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Config {What} unavailable; using the last good answer: {UsedLastGood}")]
    private static partial void ConfigUnavailable(ILogger logger, string what, bool usedLastGood, Exception ex);
}
