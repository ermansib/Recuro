using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Domain.Rules;

namespace Recuro.Offer.Infrastructure.Clients;

/// <summary>Config's <c>resolve/matrices/offer</c> response; <c>content</c> is its <c>OfferMatrix</c>.</summary>
internal sealed record OfferMatrixResponse(string ConfigVersionId, string MatrixType, int Number, OfferMatrixContent Content);

/// <summary>
/// The offer matrix. <c>rules</c> is Config's Annexure D table today; <c>ctcRules</c> and the lifecycle
/// timings are optional (requested contract) and fall back to the FRD defaults until Config carries them.
/// </summary>
internal sealed record OfferMatrixContent(
    IReadOnlyList<OfferMatrixRuleContent>? Rules,
    IReadOnlyList<CtcRuleContent>? CtcRules,
    int? ValidityWorkingDays,
    int? FirstChaseAfterWorkingDays,
    int? ChaseEveryDays);

internal sealed record OfferMatrixRuleContent(IReadOnlyList<string> Levels, ApprovalLegContent WithinBand, ApprovalLegContent Deviation);

internal sealed record ApprovalLegContent(string Label, string ApproverRole);

/// <summary>A share rule: <c>{ id, component: fixed|variable|benefits, minPercent?, maxPercent? }</c>.</summary>
internal sealed record CtcRuleContent(string Id, string Component, decimal? MinPercent, decimal? MaxPercent);

internal sealed record WorkingDaysResponse(DateOnly Date, string? ConfigVersionId);

/// <summary>
/// HTTP client for the Config service (architecture.md, "Config: resolve rules"). The offer matrix is
/// cached per tenant for a few minutes. Failures surface as <see cref="DependencyUnavailableException"/>
/// (503), never as a guessed approval route.
/// </summary>
public sealed class ConfigClient(HttpClient http, IMemoryCache cache, ITenantContext tenant, TimeProvider clock, IOptions<ServiceEndpoints> options)
    : IOfferRulesSource, IWorkingDays
{
    public async Task<OfferRules> GetAsync(CancellationToken ct)
    {
        var key = $"offer:rules:{tenant.TenantId}";
        if (cache.TryGetValue(key, out OfferRules? cached) && cached is not null)
        {
            return cached;
        }

        var at = Uri.EscapeDataString(clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        var matrix = await GetJsonAsync<OfferMatrixResponse>($"api/v1/resolve/matrices/offer?at={at}", ct);
        var rules = FromMatrix(matrix);
        cache.Set(key, rules, options.Value.CacheFor);
        return rules;
    }

    public async Task<DateOnly> AddAsync(DateOnly from, int days, CancellationToken ct)
    {
        if (days == 0)
        {
            return from;
        }

        var body = await GetJsonAsync<WorkingDaysResponse>(
            string.Create(CultureInfo.InvariantCulture, $"api/v1/resolve/working-days?from={from:yyyy-MM-dd}&days={days}"),
            ct);
        return body.Date;
    }

    internal static OfferRules FromMatrix(OfferMatrixResponse matrix)
    {
        var content = matrix.Content;
        var approvals = (content.Rules ?? [])
            .Select(r => new OfferApprovalRule(r.Levels, new ApprovalLeg(r.WithinBand.Label, r.WithinBand.ApproverRole), new ApprovalLeg(r.Deviation.Label, r.Deviation.ApproverRole)))
            .ToList();
        var ctcRules = content.CtcRules is { Count: > 0 } rows
            ? new CtcRuleSet([new NonNegativeComponentsRule(), .. rows.Select(r => new ComponentShareRule(r.Id, r.Component, r.MinPercent, r.MaxPercent))])
            : CtcRuleSet.Default;
        return new OfferRules(
            matrix.ConfigVersionId,
            approvals,
            ctcRules,
            content.ValidityWorkingDays ?? OfferRules.DefaultValidityWorkingDays,
            content.FirstChaseAfterWorkingDays ?? OfferRules.DefaultFirstChaseAfterWorkingDays,
            content.ChaseEveryDays ?? OfferRules.DefaultChaseEveryDays);
    }

    private async Task<T> GetJsonAsync<T>(string uri, CancellationToken ct)
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
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                throw new DependencyUnavailableException(
                    string.Create(CultureInfo.InvariantCulture, $"Config answered {(int)response.StatusCode}: {(detail.Length <= 300 ? detail : detail[..300])}"));
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
}
