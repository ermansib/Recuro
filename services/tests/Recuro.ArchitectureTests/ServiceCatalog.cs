using System.Reflection;

namespace Recuro.ArchitectureTests;

/// <summary>Finds the services in the repository by convention: services/{Name}/src/Recuro.{Name}.Domain.</summary>
internal static class ServiceCatalog
{
    public const string Prefix = "Recuro.";
    public const string BuildingBlocks = "Recuro.BuildingBlocks";
    public static readonly string[] Layers = ["Domain", "Application", "Infrastructure", "Api"];

    /// <summary>Folders whose projects are not a service: shared code, the edge and cross-cutting tests.</summary>
    public static readonly string[] NonServiceFolders = ["BuildingBlocks", "Gateway", "tests"];

    public static string ServicesRoot { get; } = FindServicesRoot();

    public static IReadOnlyList<string> Names { get; } = Directory.GetDirectories(ServicesRoot)
        .Select(Path.GetFileName)
        .OfType<string>()
        .Where(name => !NonServiceFolders.Contains(name, StringComparer.Ordinal))
        .Where(name => Directory.Exists(Path.Combine(ServicesRoot, name, "src", $"{Prefix}{name}.Domain")))
        .Order(StringComparer.Ordinal)
        .ToList();

    public static TheoryData<string> All()
    {
        var data = new TheoryData<string>();
        foreach (var name in Names)
        {
            data.Add(name);
        }

        return data;
    }

    public static Assembly Load(string service, string layer) => Assembly.Load($"{Prefix}{service}.{layer}");

    /// <summary>Root namespaces of every other service, which <paramref name="service"/> must never touch.</summary>
    public static string[] OtherServices(string service) =>
        Names.Where(n => n != service).Select(n => $"{Prefix}{n}").Append($"{Prefix}Gateway").ToArray();

    private static string FindServicesRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Recuro.Services.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find services/Recuro.Services.slnx.");
    }
}
