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

/// <summary>The gateway with fake Candidate and Pipeline services behind the log-candidate BFF.</summary>
public sealed class LogCandidateGatewayFactory : WebApplicationFactory<Program>
{
    public FakeCandidateServices Backends { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("Bff:Candidates:CandidatesUrl", "http://candidate.test/api/v1/candidates");
        builder.UseSetting("Bff:Candidates:ApplicationsUrl", "http://pipeline.test/api/v1/pipeline/applications");
        builder.ConfigureTestServices(services =>
            services.Configure<HttpClientFactoryOptions>("bff", o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = Backends)));
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

/// <summary>Candidate answers 409 for <c>dup@…</c>; Pipeline answers 409 for requisition <c>REQ-LOCKED</c>.</summary>
public sealed class FakeCandidateServices : HttpMessageHandler
{
    private const string ProblemJson = "application/problem+json";

    public List<SeenRequest> Seen { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var key = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null;
        lock (Seen)
        {
            Seen.Add(new SeenRequest(request.RequestUri!.Host, request.Headers.Authorization?.ToString(), key, body));
        }

        return request.RequestUri!.Host switch
        {
            "candidate.test" when body.GetProperty("email").GetString()!.StartsWith("dup@", StringComparison.Ordinal) =>
                Json(HttpStatusCode.Conflict, """{"title":"Possible duplicate","status":409,"code":"duplicate_candidate","existingId":"c-1"}""", ProblemJson),
            "candidate.test" =>
                Json(HttpStatusCode.Created, $$"""{"id":"c-2","name":{{JsonSerializer.Serialize(body.GetProperty("name").GetString())}}}"""),
            "pipeline.test" when body.GetProperty("reqId").GetString() == "REQ-LOCKED" =>
                Json(HttpStatusCode.Conflict, """{"title":"Sourcing locked","status":409,"code":"sourcing_locked"}""", ProblemJson),
            "pipeline.test" =>
                Json(HttpStatusCode.Created, $$"""{"appId":"APP-2026-0001","reqId":{{JsonSerializer.Serialize(body.GetProperty("reqId").GetString())}},"candidateId":"c-2","stage":"Sourced"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };
}

public sealed record SeenRequest(string Host, string? Authorization, string? IdempotencyKey, JsonElement Body);

public sealed class BffLogCandidateTests(LogCandidateGatewayFactory gateway) : IClassFixture<LogCandidateGatewayFactory>
{
    private static object Input(string reqId = "REQ-1", string email = "asha@mail.example") => new
    {
        reqId,
        name = "Asha Rao",
        email,
        phone = "98000 00000",
        experienceYears = 6,
        source = "Walk-in",
        privacyConsent = true,
    };

    private List<SeenRequest> SeenWithKey(string key)
    {
        lock (gateway.Backends.Seen)
        {
            return [.. gateway.Backends.Seen.Where(s => s.IdempotencyKey?.StartsWith(key, StringComparison.Ordinal) == true)];
        }
    }

    [Fact]
    public async Task One_call_creates_the_candidate_then_the_application_and_returns_a_pipeline_card()
    {
        var client = gateway.SignedIn(RecuroRoles.HrTa);
        var key = Guid.NewGuid().ToString("N");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);

        var response = await client.PostAsJsonAsync("/bff/candidates", Input());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var card = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("APP-2026-0001", card.GetProperty("application").GetProperty("appId").GetString());
        Assert.Equal("c-2", card.GetProperty("candidate").GetProperty("id").GetString());

        var seen = SeenWithKey(key);
        Assert.Equal([("candidate.test", $"{key}:candidate"), ("pipeline.test", $"{key}:application")], seen.Select(s => (s.Host, s.IdempotencyKey)));
        Assert.All(seen, s => Assert.Equal("Bearer caller-token", s.Authorization));

        var consent = Assert.Single(seen[0].Body.GetProperty("consents").EnumerateArray());
        Assert.Equal("DataPrivacy", consent.GetProperty("type").GetString());
        Assert.Equal("hr-logged", seen[0].Body.GetProperty("consentSource").GetString());
        Assert.Equal("c-2", seen[1].Body.GetProperty("candidateId").GetString());
        Assert.Equal("Walk-in", seen[1].Body.GetProperty("source").GetString());
    }

    [Fact]
    public async Task A_duplicate_candidate_comes_back_as_409_and_no_application_is_created()
    {
        var client = gateway.SignedIn(RecuroRoles.HrTa);
        var key = Guid.NewGuid().ToString("N");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);

        var response = await client.PostAsJsonAsync("/bff/candidates", Input(email: "dup@mail.example"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("duplicate_candidate", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(["candidate.test"], SeenWithKey(key).Select(s => s.Host));
    }

    [Fact]
    public async Task A_locked_requisition_comes_back_as_the_pipeline_409()
    {
        var response = await gateway.SignedIn(RecuroRoles.HrTa).PostAsJsonAsync("/bff/candidates", Input(reqId: "REQ-LOCKED"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("sourcing_locked", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Only_HR_TA_can_log_candidates()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await gateway.SignedIn(RecuroRoles.MdCeo).PostAsJsonAsync("/bff/candidates", Input())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await gateway.CreateClient().PostAsJsonAsync("/bff/candidates", Input())).StatusCode);
    }

    [Fact]
    public async Task An_idempotency_key_too_long_for_its_step_suffix_is_rejected()
    {
        var client = gateway.SignedIn(RecuroRoles.HrTa);
        client.DefaultRequestHeaders.Add("Idempotency-Key", new string('k', 89));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/bff/candidates", Input())).StatusCode);
    }
}
