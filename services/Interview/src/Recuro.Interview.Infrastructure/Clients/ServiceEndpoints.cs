namespace Recuro.Interview.Infrastructure.Clients;

/// <summary>Base addresses of the services this one calls directly (inside the network, not via the gateway).</summary>
public sealed class ServiceEndpoints
{
    public const string SectionName = "Services";

    /// <summary>Config service, e.g. <c>http://localhost:5102/</c>.</summary>
    public Uri? Config { get; set; }

    /// <summary>Identity service (PDP), e.g. <c>http://localhost:5101/</c>.</summary>
    public Uri? Identity { get; set; }

    /// <summary>Requisition service (job descriptions), e.g. <c>http://localhost:5105/</c>.</summary>
    public Uri? Requisition { get; set; }

    /// <summary>Workflow service, e.g. <c>http://localhost:5106/</c>.</summary>
    public Uri? Workflow { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a resolved interview matrix is reused.</summary>
    public TimeSpan RulesCacheFor { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the last good interview matrix stays usable while Config can't be reached.</summary>
    public TimeSpan RulesKeepStaleFor { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Points a client at a service's base address with the configured timeout.</summary>
    public void Apply(HttpClient client, Uri? address, string name)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.BaseAddress = address ?? throw new InvalidOperationException($"Services:{name} is not set.");
        client.Timeout = Timeout;
    }
}
