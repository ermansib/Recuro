using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Gateway.Tests;

/// <summary>The gateway with fake dashboard sources: requisition and reporting answer, offer fails, bgv hangs, pipeline has no URL.</summary>
public sealed class BffGatewayFactory : WebApplicationFactory<Program>
{
    public FakeSources Sources { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Bff:Dashboard:SourceTimeout", "00:00:00.500");
        builder.UseSetting("Bff:Dashboard:Sources:requisition:Url", "http://requisition.test/fragment");
        builder.UseSetting("Bff:Dashboard:Sources:offer:Url", "http://offer.test/fragment");
        builder.UseSetting("Bff:Dashboard:Sources:bgv:Url", "http://bgv.test/fragment");
        builder.UseSetting("Bff:Dashboard:Sources:pipeline:Url", string.Empty);
        builder.UseSetting("Bff:Dashboard:Sources:reporting:Url", "http://reporting.test/fragment");
        builder.UseSetting("ReverseProxy:Clusters:notification:Destinations:primary:Address", "http://127.0.0.1:1/");
        builder.ConfigureTestServices(services =>
            services.Configure<HttpClientFactoryOptions>("bff", o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = Sources)));
    }

    public HttpClient SignedIn(string role)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.UserHeader, $"user-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.RolesHeader, role);
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.TenantHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new("Bearer", "caller-token");
        return client;
    }
}

public sealed class FakeSources : HttpMessageHandler
{
    public List<string?> AuthorizationSeen { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (AuthorizationSeen)
        {
            AuthorizationSeen.Add(request.Headers.Authorization?.ToString());
        }

        switch (request.RequestUri!.Host)
        {
            case "requisition.test":
                var fragment = new
                {
                    stats = new[] { new { label = "Open MRFs", value = "14", trend = "▲ +2 this week", tone = "" } },
                    tatBreaches = new[] { new { reqId = "REQ-1", position = "Sr Manager", stage = "Sourcing 8d / 7d TAT", stageTone = "amber", escalation = "TA-Head", link = "/approvals" } },
                };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(fragment), Encoding.UTF8, "application/json") };
            case "reporting.test":
                var kpis = new
                {
                    kpis = new[] { new { name = "Offer-to-Join Ratio", target = "Target ≥ 85%", value = "87%", status = "✓", tone = "green", trend = new[] { 80.0, 87.0 } } },
                };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(kpis), Encoding.UTF8, "application/json") };
            case "bgv.test":
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            default:
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }
}

public sealed class BffDashboardTests(BffGatewayFactory gateway) : IClassFixture<BffGatewayFactory>
{
    [Fact]
    public async Task One_call_returns_every_tile_and_unavailable_sources_show_a_dash_instead_of_failing()
    {
        var response = await gateway.SignedIn(RecuroRoles.HrTa).GetAsync("/bff/dashboard/ta");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            ["dateLabel", "kpiPeriodLabel", "kpis", "pipeline", "stats", "tatBreaches", "upcoming"],
            body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

        var tiles = body.GetProperty("stats").EnumerateArray().Select(t => (t.GetProperty("label").GetString(), t.GetProperty("value").GetString())).ToList();
        Assert.Equal(
            [("Open MRFs", "14"), ("Offers Pending", "—"), ("BGV in Progress", "—"), ("Joining ≤ 30d", "—"), ("TAT Breaches", "—")],
            tiles);
        Assert.Equal("REQ-1", body.GetProperty("tatBreaches")[0].GetProperty("reqId").GetString());
        Assert.Equal("Offer-to-Join Ratio", body.GetProperty("kpis")[0].GetProperty("name").GetString());
        Assert.Equal("offer,bgv,pipeline", response.Headers.GetValues("X-Recuro-Degraded").Single());
    }

    [Fact]
    public async Task Sources_receive_the_callers_own_token()
    {
        await gateway.SignedIn(RecuroRoles.HrHead).GetAsync("/bff/dashboard/ta");

        Assert.Contains("Bearer caller-token", gateway.Sources.AuthorizationSeen);
    }

    [Fact]
    public async Task Only_HR_staff_get_the_dashboard()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await gateway.SignedIn(RecuroRoles.Candidate).GetAsync("/bff/dashboard/ta")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await gateway.CreateClient().GetAsync("/bff/dashboard/ta")).StatusCode);
    }

    [Fact]
    public async Task Notification_routes_and_the_live_stream_are_proxied_behind_sign_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await gateway.CreateClient().GetAsync("/stream/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, (await gateway.SignedIn(RecuroRoles.Candidate).GetAsync("/stream/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, (await gateway.SignedIn(RecuroRoles.HrTa).GetAsync("/api/v1/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, (await gateway.SignedIn(RecuroRoles.HrTa).GetAsync("/api/v1/notifications/unread-count")).StatusCode);
    }
}
