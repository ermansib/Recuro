using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Middleware;

namespace Recuro.Gateway.Bff;

/// <summary>
/// The frontend's <c>logCandidate</c> as one call (architecture.md, "Log candidate BFF"): create the candidate in
/// the Candidate service, then the application in the Pipeline service, and return a <c>PipelineCard</c>.
/// Each service's error comes back as is, including Candidate's 409 <c>duplicate_candidate</c> (RCU-PIP-002) and
/// Pipeline's 409 <c>sourcing_locked</c>. The client's <c>Idempotency-Key</c> goes to each step with a suffix, so a
/// retry after a partial failure replays the stored candidate and then creates the application.
/// </summary>
internal static partial class LogCandidateEndpoints
{
    public const string Path = "/bff/candidates";
    public const string Policy = "bff.candidates";
    public const string CandidateStep = ":candidate";
    public const string ApplicationStep = ":application";

    /// <summary>The services accept keys up to 100 characters; the longest suffix must still fit.</summary>
    public const int MaxClientKeyLength = 100 - 12;

    /// <summary>Who captured the consent, as the Candidate service records it.</summary>
    private const string ConsentSource = "hr-logged";

    public static IServiceCollection AddLogCandidateBff(this IServiceCollection services)
    {
        services.AddOptions<LogCandidateOptions>().BindConfiguration(LogCandidateOptions.SectionName);
        services.AddAuthorizationBuilder().AddRolePolicy(Policy, RecuroRoles.HrTa);
        return services;
    }

    public static IEndpointRouteBuilder MapLogCandidateBff(this IEndpointRouteBuilder app)
    {
        app.MapPost(Path, LogAsync)
            .RequireAuthorization(Policy)
            .RequireRateLimiting(GatewayRateLimits.PerUser)
            .WithTags("BFF")
            .WithSummary("Log a candidate against a requisition in one call (frontend logCandidate); returns a PipelineCard.");
        return app;
    }

    private static async Task<IResult> LogAsync(
        LogCandidateRequest request,
        HttpContext http,
        IHttpClientFactory clients,
        IOptions<LogCandidateOptions> options,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var key = http.Request.Headers[IdempotencyMiddleware.KeyHeader].ToString();
        if (key.Length > MaxClientKeyLength)
        {
            return Results.Problem(
                $"{IdempotencyMiddleware.KeyHeader} must be at most {MaxClientKeyLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var settings = options.Value;
        var caller = AuthenticationHeaderValue.TryParse(http.Request.Headers.Authorization.ToString(), out var header) ? header : null;
        var client = clients.CreateClient(DashboardAggregator.ClientName);
        var logger = loggers.CreateLogger(typeof(LogCandidateEndpoints));

        var candidateBody = new
        {
            request.Name,
            request.Email,
            request.Phone,
            request.ExperienceYears,
            request.Source,
            Consents = request.PrivacyConsent
                ? new[] { new { Type = "DataPrivacy", TextVersion = settings.PrivacyConsentVersion } }
                : [],
            ConsentSource,
        };
        var candidate = await SendAsync(client, settings.CandidatesUrl, candidateBody, caller, Step(key, CandidateStep), settings.StepTimeout, logger, ct);
        if (candidate.Failure is not null)
        {
            return candidate.Failure;
        }

        var candidateId = candidate.Body.TryGetProperty("id", out var id) ? id.GetString() : null;
        if (string.IsNullOrEmpty(candidateId))
        {
            return Unavailable("candidate");
        }

        var applicationBody = new { request.ReqId, CandidateId = candidateId, request.Source };
        var application = await SendAsync(client, settings.ApplicationsUrl, applicationBody, caller, Step(key, ApplicationStep), settings.StepTimeout, logger, ct);
        if (application.Failure is not null)
        {
            return application.Failure;
        }

        var appId = application.Body.TryGetProperty("appId", out var app) ? app.GetString() : null;
        return Results.Created(
            appId is null ? null : $"/api/v1/pipeline/applications/{appId}",
            new { Application = application.Body, Candidate = candidate.Body });
    }

    private static string? Step(string key, string suffix) => key.Length == 0 ? null : key + suffix;

    private static async Task<StepResult> SendAsync(
        HttpClient client,
        Uri url,
        object body,
        AuthenticationHeaderValue? caller,
        string? idempotencyKey,
        TimeSpan timeout,
        ILogger logger,
        CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: JsonSerializerOptions.Web) };
            request.Headers.Authorization = caller;
            if (idempotencyKey is not null)
            {
                request.Headers.Add(IdempotencyMiddleware.KeyHeader, idempotencyKey);
            }

            using var response = await client.SendAsync(request, budget.Token);
            var text = await response.Content.ReadAsStringAsync(budget.Token);
            if (!response.IsSuccessStatusCode)
            {
                // The service's own problem details are the contract the frontend handles; pass them through.
                var contentType = response.Content.Headers.ContentType?.ToString() ?? MediaTypeNames.Application.ProblemJson;
                return new StepResult(default, Results.Content(text, contentType, statusCode: (int)response.StatusCode));
            }

            return new StepResult(JsonSerializer.Deserialize<JsonElement>(text), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            StepFailed(logger, url.Host, ex.GetType().Name);
            return new StepResult(default, Unavailable(url.Host));
        }
    }

    private static IResult Unavailable(string source) =>
        Results.Problem($"The {source} service did not answer. Try again; the request is safe to retry with the same Idempotency-Key.", statusCode: StatusCodes.Status502BadGateway);

    private readonly record struct StepResult(JsonElement Body, IResult? Failure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Log candidate step to {Host} failed ({Reason})")]
    private static partial void StepFailed(ILogger logger, string host, string reason);
}

/// <summary>The frontend's <c>LogCandidateInput</c>.</summary>
internal sealed record LogCandidateRequest(
    string? ReqId,
    string? Name,
    string? Email,
    string? Phone,
    decimal ExperienceYears,
    string? Source,
    bool PrivacyConsent);
