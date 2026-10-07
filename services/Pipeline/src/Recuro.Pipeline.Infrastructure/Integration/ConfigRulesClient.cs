using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Pipeline.Application.Abstractions;
using Recuro.Pipeline.Application.Applications;
using Recuro.Pipeline.Domain.Applications;

namespace Recuro.Pipeline.Infrastructure.Integration;

public sealed class ConfigServiceOptions
{
    public const string SectionName = "Services:Config";

    public Uri BaseUrl { get; set; } = new("http://localhost:5102/");

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How long a resolved TAT matrix or working-day answer is reused.</summary>
    public TimeSpan CacheFor { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the last good answer stays usable while Config can't be reached.</summary>
    public TimeSpan KeepStaleFor { get; set; } = TimeSpan.FromHours(24);

    /// <summary>After a failed call, the fallback is reused this long before Config is asked again.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Config's <c>resolve/working-days</c> response.</summary>
internal sealed record WorkingDaysResponse(DateOnly Date, Guid ConfigVersionId);

/// <summary>Config's <c>resolve/matrices/tat</c> response; <c>content</c> is its <c>TatMatrix</c>.</summary>
internal sealed record TatMatrixResponse(Guid ConfigVersionId, string MatrixType, int Number, DateTimeOffset EffectiveFrom, TatMatrixContent Content);

internal sealed record TatMatrixContent(IReadOnlyList<TatStageContent> Stages);

internal sealed record TatStageContent(string Stage, string Label, int MinWorkingDays, int MaxWorkingDays, string Owner, RoleContactContent? EscalateTo);

internal sealed record RoleContactContent(string Role, string Label);

/// <summary>
/// The Config service's business calendar and TAT matrix (architecture.md "Config: resolve rules"), cached
/// per tenant. When Config can't be reached the last good answer is used, and with nothing cached the
/// local fallback: weekends-only working days and the FRD §5.2 TATs in <see cref="PipelineOptions"/>.
/// </summary>
internal sealed partial class ConfigRulesClient(
    HttpClient http,
    IMemoryCache cache,
    ITenantContext tenant,
    TimeProvider clock,
    IOptions<ConfigServiceOptions> configOptions,
    IOptions<PipelineOptions> pipelineOptions,
    ILogger<ConfigRulesClient> logger) : IWorkingDayCalendar, ITatRules
{
    /// <summary>Config TAT stage keys → the pipeline stages whose clock they set. "sourcing" covers sourcing and screening.</summary>
    private static readonly (string Key, ApplicationStage Stage)[] StageKeys =
    [
        ("sourcing", ApplicationStage.Sourced),
        ("sourcing", ApplicationStage.Screened),
        ("interview", ApplicationStage.Interview),
        ("bgv", ApplicationStage.BGV),
        ("offer-issuance", ApplicationStage.Offer),
    ];

    public Task<DateOnly> AddAsync(DateOnly from, int days, CancellationToken ct)
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
            () => WorkingDays.Add(from, days),
            ct);
    }

    public Task<IReadOnlyDictionary<ApplicationStage, StageTatRule>> GetAsync(CancellationToken ct) =>
        ResolveAsync(
            "tat",
            async () =>
            {
                var at = Uri.EscapeDataString(clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
                var matrix = await http.GetFromJsonAsync<TatMatrixResponse>(new Uri($"api/v1/resolve/matrices/tat?at={at}", UriKind.Relative), ct)
                    ?? throw new System.Text.Json.JsonException("Config returned an empty TAT matrix.");
                return FromMatrix(matrix.Content);
            },
            LocalTat,
            ct);

    internal static IReadOnlyDictionary<ApplicationStage, StageTatRule> FromMatrix(TatMatrixContent content)
    {
        var byKey = content.Stages.ToDictionary(s => s.Stage, StringComparer.Ordinal);
        var rules = new Dictionary<ApplicationStage, StageTatRule>();
        foreach (var (key, stage) in StageKeys)
        {
            // The breach clock runs to the top of the §5.2 range.
            if (byKey.TryGetValue(key, out var row))
            {
                rules[stage] = new StageTatRule { WorkingDays = row.MaxWorkingDays, Escalation = row.EscalateTo?.Role ?? string.Empty };
            }
        }

        return rules;
    }

    private IReadOnlyDictionary<ApplicationStage, StageTatRule> LocalTat()
    {
        var rules = new Dictionary<ApplicationStage, StageTatRule>();
        foreach (var (name, rule) in pipelineOptions.Value.StageTat)
        {
            if (Enum.TryParse<ApplicationStage>(name, out var stage))
            {
                rules[stage] = rule;
            }
        }

        return rules;
    }

    private async Task<T> ResolveAsync<T>(string what, Func<Task<T>> fetch, Func<T> fallback, CancellationToken ct)
        where T : notnull
    {
        var options = configOptions.Value;
        var freshKey = $"pipeline:config:{tenant.TenantId}:{what}";
        var lastGoodKey = $"{freshKey}:last-good";
        if (cache.TryGetValue(freshKey, out T? fresh) && fresh is not null)
        {
            return fresh;
        }

        try
        {
            var value = await fetch();
            cache.Set(freshKey, value, options.CacheFor);
            cache.Set(lastGoodKey, value, options.KeepStaleFor);
            return value;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            // The last good answer if there is one, otherwise the local rules; Config is asked again after RetryAfter.
            var hasLastGood = cache.TryGetValue(lastGoodKey, out T? lastGood) && lastGood is not null;
            var value = hasLastGood ? lastGood! : fallback();
            cache.Set(freshKey, value, options.RetryAfter);
            ConfigUnavailable(logger, what, hasLastGood, ex);
            return value;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Config {What} unavailable; using the last good answer: {UsedLastGood}")]
    private static partial void ConfigUnavailable(ILogger logger, string what, bool usedLastGood, Exception ex);
}
