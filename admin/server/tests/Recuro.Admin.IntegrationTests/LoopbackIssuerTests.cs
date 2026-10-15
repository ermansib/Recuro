using Recuro.Admin.Api.Auth;

namespace Recuro.Admin.IntegrationTests;

/// <summary>The main portal signs in at 127.0.0.1:8080 while the API is configured with localhost:8080.</summary>
public sealed class LoopbackIssuerTests
{
    [Fact]
    public void A_localhost_authority_also_accepts_its_127_0_0_1_name()
    {
        var issuers = LoopbackIssuers.For("http://localhost:8080/realms/recuro");

        Assert.Equal(["http://localhost:8080/realms/recuro", "http://127.0.0.1:8080/realms/recuro"], issuers);
    }

    [Fact]
    public void A_127_0_0_1_authority_also_accepts_its_localhost_name()
    {
        var issuers = LoopbackIssuers.For("http://127.0.0.1:8080/realms/recuro", null);

        Assert.Contains("http://localhost:8080/realms/recuro", issuers);
        Assert.Equal(2, issuers.Length);
    }

    [Fact]
    public void Any_other_host_is_accepted_only_as_configured()
    {
        var issuers = LoopbackIssuers.For("https://id.example.com/realms/recuro", "http://keycloak:8080/realms/recuro");

        Assert.Equal(["https://id.example.com/realms/recuro", "http://keycloak:8080/realms/recuro"], issuers);
    }
}
