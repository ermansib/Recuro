using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.BuildingBlocks.UnitTests;

public class ServiceTokenTests
{
    private static readonly Guid Tenant = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01");

    private static readonly Dictionary<string, string?> Secret = new() { ["ServiceAuth:ClientSecret"] = "secret" };

    [Fact]
    public async Task A_call_without_a_caller_is_signed_as_the_service_for_the_tenant_in_scope()
    {
        var keycloak = new StubHandler(_ => Json("""{"access_token":"svc-token","expires_in":300}"""));
        using var provider = Build(keycloak, "Production", Secret);
        var downstream = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await SendInScopeAsync(provider, downstream);

        var request = Assert.Single(downstream.Requests);
        Assert.Equal("Bearer svc-token", request.Headers.Authorization?.ToString());
        Assert.Equal(Tenant.ToString(), Assert.Single(request.Headers.GetValues(ServiceTenantHeader.Name)));
        var tokenRequest = Assert.Single(keycloak.Requests);
        Assert.Equal(new Uri("http://keycloak:8080/realms/recuro/protocol/openid-connect/token"), tokenRequest.RequestUri);
        Assert.Contains("client_id=recuro-svc-notification", keycloak.Bodies.Single(), StringComparison.Ordinal);
        Assert.Contains("grant_type=client_credentials", keycloak.Bodies.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_token_is_cached_and_fetched_again_after_a_401()
    {
        var keycloak = new StubHandler(_ => Json("""{"access_token":"svc-token","expires_in":300}"""));
        using var provider = Build(keycloak, "Production", Secret);
        var status = HttpStatusCode.OK;
        var downstream = new StubHandler(_ => new HttpResponseMessage(status));

        await SendInScopeAsync(provider, downstream);
        await SendInScopeAsync(provider, downstream);
        Assert.Single(keycloak.Requests);

        status = HttpStatusCode.Unauthorized;
        await SendInScopeAsync(provider, downstream);
        status = HttpStatusCode.OK;
        await SendInScopeAsync(provider, downstream);
        Assert.Equal(2, keycloak.Requests.Count);
    }

    [Fact]
    public async Task A_forwarded_caller_is_left_alone()
    {
        var keycloak = new StubHandler(_ => Json("""{"access_token":"svc-token","expires_in":300}"""));
        using var provider = Build(keycloak, "Production", Secret);
        var downstream = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await SendInScopeAsync(provider, downstream, request => request.Headers.TryAddWithoutValidation("Authorization", "Bearer user-token"));

        Assert.Equal("Bearer user-token", Assert.Single(downstream.Requests).Headers.Authorization?.ToString());
        Assert.Empty(keycloak.Requests);
    }

    [Fact]
    public async Task Development_auth_mode_sends_dev_headers_instead_of_a_token()
    {
        var keycloak = new StubHandler(_ => throw new InvalidOperationException("No token in Development auth mode."));
        using var provider = Build(keycloak, "Development", new Dictionary<string, string?> { ["Auth:Mode"] = "Development" });
        var downstream = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await SendInScopeAsync(provider, downstream);

        var request = Assert.Single(downstream.Requests);
        Assert.Equal("recuro-svc-notification", request.Headers.GetValues(DevelopmentAuthenticationHandler.UserHeader).Single());
        Assert.Equal("service", request.Headers.GetValues(DevelopmentAuthenticationHandler.RolesHeader).Single());
        Assert.Equal(Tenant.ToString(), request.Headers.GetValues(DevelopmentAuthenticationHandler.TenantHeader).Single());
    }

    [Fact]
    public void Development_uses_the_realm_files_local_secret_and_other_environments_do_not()
    {
        using var development = Build(new StubHandler(_ => Json("{}")), "Development");
        using var production = Build(new StubHandler(_ => Json("{}")), "Production", new Dictionary<string, string?> { ["ServiceAuth:ClientSecret"] = null });

        Assert.Equal("recuro-svc-notification-dev-secret", development.GetRequiredService<IOptions<ServiceAuthOptions>>().Value.ClientSecret);
        Assert.Null(production.GetRequiredService<IOptions<ServiceAuthOptions>>().Value.ClientSecret);
    }

    [Theory]
    [InlineData("service", null, true)]
    [InlineData("hrta", null, false)]
    [InlineData("service", "6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a02", false)]
    public async Task Only_a_service_token_without_a_tenant_takes_the_tenant_header(string role, string? tokenTenant, bool takesHeader)
    {
        var claims = new List<Claim> { new(RecuroClaims.Subject, "caller"), new(RecuroClaims.Roles, role) };
        if (tokenTenant is not null)
        {
            claims.Add(new Claim(RecuroClaims.Tenant, tokenTenant));
        }

        var http = new DefaultHttpContext();
        http.Request.Headers[ServiceTenantHeader.Name] = Tenant.ToString();
        var context = new TokenValidatedContext(http, new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
        };

        await ServiceTenantHeader.ApplyAsync(context);

        var tenants = context.Principal!.FindAll(RecuroClaims.Tenant).Select(c => c.Value).ToList();
        Assert.Equal(takesHeader ? [Tenant.ToString()] : tokenTenant is null ? [] : [tokenTenant], tenants);
    }

    private static ServiceProvider Build(StubHandler keycloak, string environment, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Auth:Authority"] = "http://localhost:8080/realms/recuro",
            ["Auth:MetadataAddress"] = "http://keycloak:8080/realms/recuro/.well-known/openid-configuration",
        };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddRecuroServiceTokens(configuration, new TestEnvironment(environment), "notification");
        services.AddHttpClient(ServiceAuthOptions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => keycloak);
        return services.BuildServiceProvider();
    }

    private static async Task SendInScopeAsync(ServiceProvider provider, StubHandler downstream, Action<HttpRequestMessage>? prepare = null)
    {
        var scope = new ScopeContext();
        scope.SetTenant(Tenant);
        scope.MakeCurrent();

        var handler = provider.GetRequiredService<ServiceTokenHandler>();
        handler.InnerHandler = downstream;
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://candidate/api/v1/candidates/1");
        prepare?.Invoke(request);
        using var response = await invoker.SendAsync(request, CancellationToken.None);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
