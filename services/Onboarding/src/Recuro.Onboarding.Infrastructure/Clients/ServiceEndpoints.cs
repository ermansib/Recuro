namespace Recuro.Onboarding.Infrastructure.Clients;

/// <summary>Base addresses of the services this one calls directly (inside the network, not via the gateway).</summary>
public sealed class ServiceEndpoints
{
    public const string SectionName = "Services";

    /// <summary>Config service (onboarding matrix, working days), e.g. <c>http://localhost:5102/</c>.</summary>
    public Uri? Config { get; set; }

    /// <summary>Candidate service (name on the confirmation letter), e.g. <c>http://localhost:5107/</c>.</summary>
    public Uri? Candidate { get; set; }

    /// <summary>Identity service (reporting manager, department head), e.g. <c>http://localhost:5101/</c>.</summary>
    public Uri? Identity { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a resolved onboarding template is reused (architecture.md: up to 5 minutes).</summary>
    public TimeSpan CacheFor { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Points a client at a service's base address with the configured timeout.</summary>
    public void Apply(HttpClient client, Uri? address, string name)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.BaseAddress = address ?? throw new InvalidOperationException($"Services:{name} is not set.");
        client.Timeout = Timeout;
    }
}
