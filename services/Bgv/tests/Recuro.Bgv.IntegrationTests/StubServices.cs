using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;

namespace Recuro.Bgv.IntegrationTests;

/// <summary>
/// Stands in for Config (resolve endpoints), Vendor (status), Workflow (start) and Identity (masking),
/// routed by path. Records workflow starts and the headers each call carried.
/// </summary>
public sealed class StubServices : HttpMessageHandler
{
    public const string ActiveAgency = "0199b5a0-0000-7000-8000-000000000001";
    public const string OtherAgency = "0199b5a0-0000-7000-8000-000000000002";
    public const string Consultant = "0199b5a0-0000-7000-8000-000000000003";
    public const string OffAgency = "0199b5a0-0000-7000-8000-000000000004";

    public bool ConfigDown { get; set; }

    /// <summary>Roles whose masking map hides <c>sensitiveNote</c>; null makes Identity answer 404 for every role.</summary>
    public HashSet<string>? RolesHidingSensitive { get; set; } = ["mdceo", "service"];

    public ConcurrentQueue<JsonElement> WorkflowStarts { get; } = new();

    public ConcurrentQueue<string?> ForwardedRoles { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = request.RequestUri!.AbsolutePath;
        ForwardedRoles.Enqueue(request.Headers.TryGetValues("X-Dev-Roles", out var roles) ? roles.FirstOrDefault() : null);

        if (path.StartsWith("/api/v1/resolve/", StringComparison.Ordinal))
        {
            return ConfigDown ? throw new HttpRequestException("Config is down (test).") : Json(Config(path, HttpUtility.ParseQueryString(request.RequestUri.Query)));
        }

        if (path.StartsWith("/api/v1/vendors/", StringComparison.Ordinal) && path.EndsWith("/status", StringComparison.Ordinal))
        {
            var id = path.Split('/')[4];
            object? vendor = id switch
            {
                ActiveAgency => new { vendorId = id, status = "active", active = true, name = "AuthBridge", type = "BgvAgency" },
                OtherAgency => new { vendorId = id, status = "active", active = true, name = "VerifyPro", type = "BgvAgency" },
                Consultant => new { vendorId = id, status = "active", active = true, name = "ABC Search", type = "Consultant" },
                OffAgency => new { vendorId = id, status = "off", active = false, name = "OldCheck", type = "BgvAgency" },
                _ => null,
            };
            return vendor is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(vendor);
        }

        if (path == "/api/v1/workflows" && request.Method == HttpMethod.Post)
        {
            WorkflowStarts.Enqueue(await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new { id = Guid.CreateVersion7() }) };
        }

        if (path.StartsWith("/api/v1/identity/masking/", StringComparison.Ordinal))
        {
            var role = path.Split('/')[5];
            if (RolesHidingSensitive is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var fields = RolesHidingSensitive.Contains(role) ? new Dictionary<string, string> { ["sensitiveNote"] = "hide" } : [];
            return Json(new { resource = "bgvCheck", role, version = "test", fields });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static object? Config(string path, System.Collections.Specialized.NameValueCollection query) => path switch
    {
        "/api/v1/resolve/working-days" => new
        {
            date = AddWeekdays(DateOnly.Parse(query["from"]!, CultureInfo.InvariantCulture), int.Parse(query["days"]!, CultureInfo.InvariantCulture)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            configVersionId = Guid.Empty,
        },
        "/api/v1/resolve/matrices/bgv" => Matrix("Bgv", new
        {
            checks = new object[]
            {
                new { type = "identity", label = "Identity & Address", detail = "National ID", condition = "All employees — always" },
                new { type = "employment", label = "Employment / Reference", detail = "Last 2 employers", condition = "All lateral hires" },
                new { type = "police", label = "Police / Antecedent", detail = "Customer-facing", condition = "Flagged roles" },
                new { type = "fitProper", label = "Fit & Proper Declaration", detail = "KMP only", condition = "KMP / Senior Management" },
                new { type = "coi", label = "Conflict of Interest", detail = "Relationships", condition = "All" },
            },
        }),
        "/api/v1/resolve/matrices/tat" => Matrix("Tat", new
        {
            stages = new object[] { new { stage = "bgv", label = "BGV completion", minWorkingDays = 10, maxWorkingDays = 12, owner = "HR", escalateTo = new { role = "hrhead", label = "HR Head" } } },
        }),
        "/api/v1/resolve/matrices/escalation" => Matrix("Escalation", new
        {
            issues = new object[]
            {
                new { issue = "adverse-bgv", label = "Adverse BGV finding", first = new { role = "hrhead", label = "HR Head + Compliance" }, final = new { role = "mdceo", label = "MD/CEO" } },
            },
        }),
        _ => null,
    };

    private static object Matrix(string type, object content) =>
        new { configVersionId = "cfg-" + type.ToLowerInvariant() + "-7", matrixType = type, number = 7, effectiveFrom = "2026-01-01T00:00:00Z", content };

    private static HttpResponseMessage Json(object? body) =>
        body is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    public static DateOnly AddWeekdays(DateOnly from, int days)
    {
        var day = from;
        for (var added = 0; added < days;)
        {
            day = day.AddDays(1);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                added++;
            }
        }

        return day;
    }
}
