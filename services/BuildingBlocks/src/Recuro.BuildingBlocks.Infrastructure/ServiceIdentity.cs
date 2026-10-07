namespace Recuro.BuildingBlocks.Infrastructure;

/// <summary>Who this service is on the bus. Bound from the <c>Service</c> configuration section.</summary>
public sealed class ServiceIdentity
{
    public const string SectionName = "Service";

    /// <summary>Short lowercase name, e.g. <c>audit</c>. Also the name of the service's queue.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>CloudEvents source, e.g. <c>/services/audit</c>.</summary>
    public string Source => $"/services/{Name}";
}
