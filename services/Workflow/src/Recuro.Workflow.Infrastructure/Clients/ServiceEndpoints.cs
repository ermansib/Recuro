namespace Recuro.Workflow.Infrastructure.Clients;

/// <summary>Base addresses of the services this one calls directly (inside the network, not via the gateway).</summary>
public sealed class ServiceEndpoints
{
    public const string SectionName = "Services";

    /// <summary>Config service, e.g. <c>http://localhost:5102/</c>.</summary>
    public Uri? Config { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Points a client at a service's base address with the configured timeout.</summary>
    public void Apply(HttpClient client, Uri? address, string name)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.BaseAddress = address ?? throw new InvalidOperationException($"Services:{name} is not set.");
        client.Timeout = Timeout;
    }
}
