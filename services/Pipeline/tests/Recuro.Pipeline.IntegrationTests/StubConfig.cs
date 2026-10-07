using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Web;

namespace Recuro.Pipeline.IntegrationTests;

/// <summary>
/// Stands in for the Config service's resolve endpoints. Its calendar has one extra holiday so tests can
/// tell Config's answer from the local weekends-only fallback. Set <see cref="Down"/> to simulate an outage.
/// </summary>
public sealed class StubConfig : HttpMessageHandler
{
    public static readonly DateOnly Holiday = new(2026, 10, 2);

    public bool Down { get; set; }

    /// <summary>The <c>sourcing</c> stage's maximum working days in the TAT matrix.</summary>
    public int SourcingMaxDays { get; set; } = 7;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Down)
        {
            throw new HttpRequestException("Config is down (test).");
        }

        var uri = request.RequestUri!;
        var query = HttpUtility.ParseQueryString(uri.Query);
        object? body = uri.AbsolutePath switch
        {
            "/api/v1/resolve/working-days" => new
            {
                date = Add(DateOnly.Parse(query["from"]!, CultureInfo.InvariantCulture), int.Parse(query["days"]!, CultureInfo.InvariantCulture)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                configVersionId = Guid.Empty,
            },
            "/api/v1/resolve/matrices/tat" => new
            {
                configVersionId = Guid.Empty,
                matrixType = "Tat",
                number = 1,
                effectiveFrom = "2026-01-01T00:00:00Z",
                content = new
                {
                    stages = new object[]
                    {
                        new { stage = "mrf-approval", label = "MRF approval", minWorkingDays = 2, maxWorkingDays = 2, owner = "HOD", escalateTo = new { role = "hrhead", label = "HR Head" } },
                        new { stage = "sourcing", label = "Sourcing", minWorkingDays = 1, maxWorkingDays = SourcingMaxDays, owner = "HR-TA", escalateTo = new { role = "hrhead", label = "HR Head" } },
                        new { stage = "interview", label = "Interview", minWorkingDays = 5, maxWorkingDays = 5, owner = "Panel", escalateTo = new { role = "hod", label = "HOD" } },
                        new { stage = "offer-to-joining", label = "Joining", minWorkingDays = 15, maxWorkingDays = 60, owner = "HR", escalateTo = (object?)null },
                    },
                },
            },
            _ => null,
        };

        return Task.FromResult(body is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
    }

    public static DateOnly Add(DateOnly from, int days)
    {
        var day = from;
        for (var added = 0; added < days;)
        {
            day = day.AddDays(1);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && day != Holiday)
            {
                added++;
            }
        }

        return day;
    }
}
