using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Recuro.BuildingBlocks.Domain;
using Recuro.Careers.Application.Abstractions;

namespace Recuro.Careers.Infrastructure.Clients;

/// <summary>Base URL and timeout of one downstream service, bound from <c>Services:{Name}</c>.</summary>
public sealed class DownstreamOptions
{
    public Uri BaseUrl { get; set; } = new("http://localhost/");

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// The Candidate service's create contract (<c>POST /api/v1/candidates</c>, RCU-CND-001/002/005), called as
/// this service. A 409 <c>duplicate_candidate</c> means the person is already on file; the intake goes on
/// with that record, as HR-TA's log-candidate flow does.
/// </summary>
internal sealed partial class CandidateIntakeClient(HttpClient http) : ICandidateIntake
{
    private const string ConsentSource = "careers-site";

    public async Task<IntakeOutcome<CandidateRef>> CreateAsync(NewCandidate candidate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var body = new
        {
            name = candidate.Name,
            email = candidate.Email,
            phone = candidate.Phone,
            experienceYears = candidate.ExperienceYears,
            summary = string.Create(CultureInfo.InvariantCulture, $"{candidate.ExperienceYears:0.#} yrs · career portal"),
            currentCtc = candidate.CurrentCtc,
            expectedCtc = candidate.ExpectedCtc,
            noticeDays = candidate.NoticeDays,
            source = "Portal",
            channel = candidate.Channel,
            consents = candidate.Consents.Select(c => new { type = c.Type, textVersion = c.TextVersion, at = c.At }),
            consentSource = ConsentSource,
        };

        using var response = await Downstream.SendAsync(http, HttpMethod.Post, "api/v1/candidates", body, "Candidate", ct);
        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return IntakeOutcome.Ok<CandidateRef>(new CandidateRef(created.GetProperty("id").GetString()!, CreatedHere: true));
        }

        var problem = await Downstream.ReadProblemAsync(response, "Candidate", ct);
        if (problem.Code == "duplicate_candidate" && ExistingId().Match(problem.Message) is { Success: true } match)
        {
            return IntakeOutcome.Ok<CandidateRef>(new CandidateRef(match.Groups[1].Value, CreatedHere: false));
        }

        return IntakeOutcome.Refused<CandidateRef>(problem);
    }

    /// <summary>
    /// Saga compensation. The Candidate service's tombstone endpoint is a requested contract; until it
    /// exists the call answers 404/405 and the candidate is left for the retention purge (RCU-CND-003).
    /// </summary>
    public async Task<bool> TombstoneAsync(string candidateId, string reason, CancellationToken ct)
    {
        using var response = await Downstream.SendAsync(
            http, HttpMethod.Post, $"api/v1/candidates/{Uri.EscapeDataString(candidateId)}/tombstone", new { reason }, "Candidate", ct);
        return response.IsSuccessStatusCode;
    }

    /// <summary>The existing record's id in the duplicate message, e.g. "... already exists (0199...)."</summary>
    [GeneratedRegex(@"\(([0-9a-fA-F-]{36})\)", RegexOptions.CultureInvariant)]
    private static partial Regex ExistingId();
}

/// <summary>The Pipeline service's create contract (<c>POST /api/v1/pipeline/applications</c>, RCU-PPL-001), called as this service.</summary>
internal sealed class PipelineIntakeClient(HttpClient http) : IPipelineIntake
{
    public async Task<IntakeOutcome<string>> CreateApplicationAsync(string reqId, string candidateId, string source, string note, CancellationToken ct)
    {
        using var response = await Downstream.SendAsync(
            http, HttpMethod.Post, "api/v1/pipeline/applications", new { reqId, candidateId, source, note }, "Pipeline", ct);
        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return IntakeOutcome.Ok<string>(created.GetProperty("appId").GetString()!);
        }

        return IntakeOutcome.Refused<string>(await Downstream.ReadProblemAsync(response, "Pipeline", ct));
    }
}

/// <summary>Shared plumbing: unreachable or failing (5xx, 401, 403) services become <see cref="DependencyUnavailableException"/>.</summary>
internal static class Downstream
{
    public static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string path, object body, string service, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };
            return await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException($"The {service} service is unreachable.", ex);
        }
    }

    /// <summary>A 4xx the caller can act on as an <see cref="Error"/>; anything else is an outage.</summary>
    public static async Task<Error> ReadProblemAsync(HttpResponseMessage response, string service, CancellationToken ct)
    {
        var status = response.StatusCode;
        if (status is not (HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict))
        {
            // 401/403 mean this service's own credentials were refused: an outage, not the applicant's fault.
            throw new DependencyUnavailableException(string.Create(CultureInfo.InvariantCulture, $"The {service} service answered {(int)status}."));
        }

        JsonElement problem;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        catch (JsonException)
        {
            problem = default;
        }

        var code = Text(problem, "code") ?? "rejected";
        var message = Text(problem, "title") ?? $"The {service} service refused the request.";
        return status switch
        {
            HttpStatusCode.BadRequest => Error.Validation(FieldErrors(problem)) with { Code = code },
            HttpStatusCode.NotFound => Error.NotFound(code, message),
            _ => Error.Conflict(code, message),
        };
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static List<FieldError> FieldErrors(JsonElement problem)
    {
        var fields = new List<FieldError>();
        if (problem.ValueKind == JsonValueKind.Object && problem.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var error in errors.EnumerateArray())
            {
                fields.Add(new FieldError(Text(error, "field") ?? string.Empty, Text(error, "code") ?? "invalid", Text(error, "message") ?? string.Empty));
            }
        }

        return fields;
    }
}
