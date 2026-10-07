using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Middleware;

namespace Recuro.Gateway.Tests;

public sealed class GatewayFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Gateway:RateLimits:PerUserPermits", "2");
        // Nothing listens here: proxied calls end in 502, which is enough to test the edge.
        builder.UseSetting("ReverseProxy:Clusters:audit:Destinations:primary:Address", "http://127.0.0.1:1/");
    }

    public HttpClient SignedIn(string user)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.UserHeader, user);
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.RolesHeader, RecuroRoles.HrTa);
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.TenantHeader, Guid.NewGuid().ToString());
        return client;
    }
}

public sealed class GatewayTests(GatewayFactory gateway) : IClassFixture<GatewayFactory>
{
    [Fact]
    public async Task Unauthenticated_requests_stop_at_the_gateway_with_401()
    {
        var response = await gateway.CreateClient().GetAsync("/api/v1/audit");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_without_a_tenant_is_refused()
    {
        var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Add(DevelopmentAuthenticationHandler.UserHeader, "no-tenant");

        var response = await client.GetAsync("/api/v1/audit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Signed_in_requests_are_proxied()
    {
        var response = await gateway.SignedIn("proxy-user").GetAsync("/api/v1/audit");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Over_quota_callers_get_429_with_Retry_After()
    {
        var client = gateway.SignedIn("busy-user");

        await client.GetAsync("/api/v1/audit");
        await client.GetAsync("/api/v1/audit");
        var third = await client.GetAsync("/api/v1/audit");

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.NotNull(third.Headers.RetryAfter);
    }

    [Fact]
    public async Task The_gateway_generates_a_request_id_and_keeps_the_callers_correlation_id()
    {
        var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationMiddleware.CorrelationIdHeader, "from-browser");

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("from-browser", response.Headers.GetValues(CorrelationMiddleware.CorrelationIdHeader).Single());
        Assert.False(string.IsNullOrEmpty(response.Headers.GetValues(CorrelationMiddleware.RequestIdHeader).Single()));
    }
}
