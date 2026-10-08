using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Web;

namespace Recuro.Onboarding.IntegrationTests;

/// <summary>
/// Stands in for Config (resolve endpoints) and Candidate (name), routed by path. Like the real Config
/// today, it has no onboarding matrix and refuses negative working days unless a test says otherwise.
/// Records the role each call carried.
/// </summary>
public sealed class StubServices : HttpMessageHandler
{
    public bool ConfigDown { get; set; }

    /// <summary>When set, Config serves an onboarding matrix with this version id and a three-item checklist.</summary>
    public string? OnboardingMatrixVersion { get; set; }

    /// <summary>When true, Config counts working days backwards (weekends only) like the requested contract.</summary>
    public bool BackwardWorkingDays { get; set; }

    public ConcurrentQueue<string?> CandidateCallRoles { get; } = new();

    public ConcurrentQueue<string?> ConfigCallRoles { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = request.RequestUri!.AbsolutePath;
        var role = request.Headers.TryGetValues("X-Dev-Roles", out var roles) ? roles.FirstOrDefault() : null;

        if (path.StartsWith("/api/v1/resolve/", StringComparison.Ordinal))
        {
            ConfigCallRoles.Enqueue(role);
            if (ConfigDown)
            {
                throw new HttpRequestException("Config is down (test).");
            }

            return Task.FromResult(Config(path, HttpUtility.ParseQueryString(request.RequestUri.Query)));
        }

        if (path.StartsWith("/api/v1/candidates/", StringComparison.Ordinal))
        {
            CandidateCallRoles.Enqueue(role);
            return Task.FromResult(Json(new { id = path.Split('/')[4], name = "J. Nair", email = "j.nair@example.test" }));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private HttpResponseMessage Config(string path, System.Collections.Specialized.NameValueCollection query)
    {
        if (path == "/api/v1/resolve/working-days")
        {
            var from = DateOnly.Parse(query["from"]!, CultureInfo.InvariantCulture);
            var days = int.Parse(query["days"]!, CultureInfo.InvariantCulture);
            if (days < 0 && !BackwardWorkingDays)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            return Json(new { date = Weekdays(from, days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), configVersionId = Guid.Empty });
        }

        if (path == "/api/v1/resolve/matrices/onboarding" && OnboardingMatrixVersion is { } version)
        {
            return Json(new
            {
                configVersionId = version,
                matrixType = "Onboarding",
                number = 3,
                effectiveFrom = "2026-01-01T00:00:00Z",
                content = new
                {
                    checklist = new[]
                    {
                        new { key = "laptop", label = "Laptop issued" },
                        new { key = "badge", label = "Badge issued" },
                        new { key = "induction", label = "Induction booked" },
                    },
                    probationMonths = 3,
                },
            });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    /// <summary>Weekdays forwards (days &gt; 0) or backwards (days &lt; 0), the start day never counting.</summary>
    public static DateOnly Weekdays(DateOnly from, int days)
    {
        var step = Math.Sign(days);
        var day = from;
        for (var counted = 0; counted < Math.Abs(days);)
        {
            day = day.AddDays(step);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                counted++;
            }
        }

        return day;
    }
}
