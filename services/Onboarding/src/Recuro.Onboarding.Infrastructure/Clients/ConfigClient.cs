using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Onboarding.Application.Abstractions;
using Recuro.Onboarding.Domain.Rules;

namespace Recuro.Onboarding.Infrastructure.Clients;

/// <summary>Config's <c>resolve/matrices/onboarding</c> response.</summary>
internal sealed record OnboardingMatrixResponse(string ConfigVersionId, string MatrixType, int Number, OnboardingMatrixContent Content);

/// <summary>
/// The onboarding matrix (requested from Config). Every part is optional: what Config leaves out
/// comes from the FRD table (<see cref="OnboardingTemplate.Default"/>).
/// </summary>
internal sealed record OnboardingMatrixContent(
    IReadOnlyList<ChecklistItemContent>? Checklist,
    IReadOnlyList<DocumentContent>? Documents,
    IReadOnlyList<int>? EngagementDaysBefore,
    int? ProvisioningWorkingDaysBefore,
    int? ProbationMonths,
    int? CheckInDay,
    int? ReviewDay,
    int? ReviewWindowEndDay);

internal sealed record ChecklistItemContent(string Key, string Label);

internal sealed record DocumentContent(string Type, string Label, bool Mandatory);

internal sealed record WorkingDaysResponse(DateOnly Date);

/// <summary>
/// HTTP client for the Config service (architecture.md, "Config: resolve rules"). The onboarding
/// template is cached per tenant for a few minutes. While Config has no onboarding matrix (404/400) the
/// FRD table applies; while Config doesn't count working days backwards (400), weekends-only counting
/// applies. Config not answering is <see cref="DependencyUnavailableException"/>: the event is retried
/// rather than planned from a guess.
/// </summary>
internal sealed partial class ConfigClient(
    HttpClient http,
    IMemoryCache cache,
    ITenantContext tenant,
    TimeProvider clock,
    IOptions<ServiceEndpoints> options,
    ILogger<ConfigClient> logger) : IOnboardingRules, IWorkingDays
{
    public async Task<OnboardingTemplate> GetTemplateAsync(CancellationToken ct)
    {
        var key = $"onboarding:template:{tenant.TenantId}";
        if (cache.TryGetValue(key, out OnboardingTemplate? cached) && cached is not null)
        {
            return cached;
        }

        var at = Uri.EscapeDataString(clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        var matrix = await GetJsonAsync<OnboardingMatrixResponse>($"api/v1/resolve/matrices/onboarding?at={at}", ct);
        var template = matrix is null ? OnboardingTemplate.Default : FromMatrix(matrix);
        if (matrix is null)
        {
            NoOnboardingMatrix(logger, tenant.TenantId);
        }

        cache.Set(key, template, options.Value.CacheFor);
        return template;
    }

    public async Task<DateOnly> SubtractAsync(DateOnly from, int days, CancellationToken ct)
    {
        if (days == 0)
        {
            return from;
        }

        var body = await GetJsonAsync<WorkingDaysResponse>(
            string.Create(CultureInfo.InvariantCulture, $"api/v1/resolve/working-days?from={from:yyyy-MM-dd}&days={-days}"),
            ct);
        if (body is not null)
        {
            return body.Date;
        }

        NoBackwardCount(logger, tenant.TenantId);
        return WeekdaysBefore(from, days);
    }

    internal static OnboardingTemplate FromMatrix(OnboardingMatrixResponse matrix)
    {
        var c = matrix.Content;
        var d = OnboardingTemplate.Default;
        return new OnboardingTemplate(
            matrix.ConfigVersionId,
            c.Checklist is { Count: > 0 } items ? items.Select(i => new ChecklistTemplateItem(i.Key, i.Label)).ToList() : d.Checklist,
            c.Documents is { Count: > 0 } docs ? docs.Select(x => new DocumentTemplate(x.Type, x.Label, x.Mandatory)).ToList() : d.Documents,
            c.EngagementDaysBefore ?? d.EngagementDaysBefore,
            c.ProvisioningWorkingDaysBefore ?? d.ProvisioningWorkingDaysBefore,
            c.ProbationMonths ?? d.ProbationMonths,
            c.CheckInDay ?? d.CheckInDay,
            c.ReviewDay ?? d.ReviewDay,
            c.ReviewWindowEndDay ?? d.ReviewWindowEndDay);
    }

    /// <summary>Weekends-only fallback, the same one Pipeline uses while Config can't answer.</summary>
    internal static DateOnly WeekdaysBefore(DateOnly from, int days)
    {
        var day = from;
        for (var counted = 0; counted < days;)
        {
            day = day.AddDays(-1);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                counted++;
            }
        }

        return day;
    }

    /// <summary>The parsed body; null when Config answers 400 or 404 (it doesn't support this yet).</summary>
    private async Task<T?> GetJsonAsync<T>(string uri, CancellationToken ct)
        where T : class
    {
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
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DependencyUnavailableException(string.Create(CultureInfo.InvariantCulture, $"Config answered {(int)response.StatusCode}."));
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new DependencyUnavailableException("Config returned an empty answer.");
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new DependencyUnavailableException("Config returned an answer this service can't read.", ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Config has no onboarding matrix for tenant {TenantId}; using the FRD Annexure E table")]
    private static partial void NoOnboardingMatrix(ILogger logger, Guid? tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Config can't count working days backwards for tenant {TenantId}; counting weekends only")]
    private static partial void NoBackwardCount(ILogger logger, Guid? tenantId);
}
