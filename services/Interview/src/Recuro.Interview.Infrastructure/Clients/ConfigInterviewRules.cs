using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Interview.Application.Abstractions;
using Recuro.Interview.Domain.Rules;

namespace Recuro.Interview.Infrastructure.Clients;

/// <summary>Config's <c>resolve/matrices/interview</c> response (requested contract; see the service README).</summary>
internal sealed record InterviewMatrixResponse(string ConfigVersionId, string MatrixType, int Number, InterviewMatrixContent Content);

internal sealed record InterviewMatrixContent(
    IReadOnlyList<GradeTemplateContent>? Templates,
    FeedbackContent? Feedback,
    RatificationContent? Ratification);

internal sealed record GradeTemplateContent(string Grade, IReadOnlyList<RoundTemplate> Rounds);

internal sealed record FeedbackContent(int ReminderAfterHours, int OverdueAfterHours);

internal sealed record RatificationContent(IReadOnlyList<string> Grades, string Role, string Label, int SlaWorkingDays);

/// <summary>
/// The tenant's interview rules from Config, cached per tenant. Until Config publishes an
/// <c>interview</c> matrix (Config answers 400/404) the FRD defaults apply; while Config can't be reached
/// the last good answer is used, and with nothing cached the FRD defaults (the Pipeline precedent).
/// </summary>
public sealed partial class ConfigInterviewRules(
    HttpClient http,
    IMemoryCache cache,
    ITenantContext tenant,
    TimeProvider clock,
    IOptions<ServiceEndpoints> options,
    ILogger<ConfigInterviewRules> logger) : IInterviewRulesSource
{
    public async Task<InterviewRules> GetAsync(CancellationToken ct)
    {
        var freshKey = $"interview:rules:{tenant.TenantId}";
        var lastGoodKey = $"{freshKey}:last-good";
        if (cache.TryGetValue(freshKey, out InterviewRules? fresh) && fresh is not null)
        {
            return fresh;
        }

        try
        {
            var at = Uri.EscapeDataString(clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
            using var response = await http.GetAsync(new Uri($"api/v1/resolve/matrices/interview?at={at}", UriKind.Relative), ct);
            InterviewRules rules;
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                rules = InterviewRules.FrdDefault;
            }
            else
            {
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadFromJsonAsync<InterviewMatrixResponse>(ct)
                    ?? throw new System.Text.Json.JsonException("Config returned an empty interview matrix.");
                rules = FromMatrix(body);
            }

            cache.Set(freshKey, rules, options.Value.RulesCacheFor);
            cache.Set(lastGoodKey, rules, options.Value.RulesKeepStaleFor);
            return rules;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            var hasLastGood = cache.TryGetValue(lastGoodKey, out InterviewRules? lastGood) && lastGood is not null;
            ConfigUnavailable(logger, hasLastGood, ex);
            return hasLastGood ? lastGood! : InterviewRules.FrdDefault;
        }
    }

    /// <summary>Missing sections fall back to the FRD defaults section by section.</summary>
    internal static InterviewRules FromMatrix(InterviewMatrixResponse matrix)
    {
        var defaults = InterviewRules.FrdDefault;
        var content = matrix.Content;
        var templates = content.Templates is { Count: > 0 } rows
            ? rows.ToDictionary(t => t.Grade, t => (IReadOnlyList<RoundTemplate>)t.Rounds.ToList(), StringComparer.Ordinal)
            : defaults.Templates;
        return new InterviewRules(
            matrix.ConfigVersionId,
            templates,
            content.Feedback is { } f ? TimeSpan.FromHours(f.ReminderAfterHours) : defaults.ReminderAfter,
            content.Feedback is { } g ? TimeSpan.FromHours(g.OverdueAfterHours) : defaults.OverdueAfter,
            content.Ratification is { } r ? new RatificationRule(r.Grades, r.Role, r.Label, r.SlaWorkingDays) : defaults.Ratification);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Config interview matrix unavailable; using the last good answer: {UsedLastGood}")]
    private static partial void ConfigUnavailable(ILogger logger, bool usedLastGood, Exception ex);
}
