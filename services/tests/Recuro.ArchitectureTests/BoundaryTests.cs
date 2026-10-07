using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.ArchitectureTests;

/// <summary>
/// Microservice boundaries: a service talks to another only over HTTP contracts or integration
/// events, never by referencing its code or reading its database.
/// </summary>
public class BoundaryTests
{
    [Theory]
    [MemberData(nameof(ServiceCatalog.All), MemberType = typeof(ServiceCatalog))]
    public void A_service_never_references_another_services_code(string service)
    {
        foreach (var layer in ServiceCatalog.Layers)
        {
            LayerTests.AssertNoDependency(ServiceCatalog.Load(service, layer), ServiceCatalog.OtherServices(service));
        }
    }

    [Fact]
    public void Project_references_stay_inside_their_own_service_or_point_at_BuildingBlocks()
    {
        var root = ServiceCatalog.ServicesRoot;
        var violations = new List<string>();
        foreach (var project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            var owner = Path.GetRelativePath(root, project).Split(Path.DirectorySeparatorChar)[0];
            if (owner is "tests" or "BuildingBlocks")
            {
                continue;
            }

            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar);
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include));
                var targetOwner = Path.GetRelativePath(root, target).Split(Path.DirectorySeparatorChar)[0];
                if (targetOwner != owner && targetOwner != "BuildingBlocks")
                {
                    violations.Add($"{Path.GetRelativePath(root, project)} → {include}");
                }
            }
        }

        Assert.True(violations.Count == 0, "Cross-service project references: " + string.Join("; ", violations));
    }

    [Fact]
    public void Shared_code_never_references_a_service()
    {
        var root = ServiceCatalog.ServicesRoot;
        var violations = Directory.EnumerateFiles(Path.Combine(root, "BuildingBlocks"), "*.csproj", SearchOption.AllDirectories)
            .SelectMany(project => XDocument.Load(project).Descendants("ProjectReference")
                .Select(r => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, r.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)))))
            .Where(target => Path.GetRelativePath(root, target).Split(Path.DirectorySeparatorChar)[0] != "BuildingBlocks")
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Each_service_has_its_own_database()
    {
        var databases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in ServiceCatalog.Names)
        {
            var settings = Path.Combine(ServiceCatalog.ServicesRoot, service, "src", $"Recuro.{service}.Api", "appsettings.Development.json");
            using var json = JsonDocument.Parse(File.ReadAllText(settings));
            var connections = json.RootElement.GetProperty("ConnectionStrings").EnumerateObject()
                .Where(c => c.Name is not ("rabbitmq" or "redis"))
                .ToList();
            Assert.True(connections.Count > 0, $"{service} has no database connection string.");

            foreach (var connection in connections)
            {
                var database = new Npgsql.NpgsqlConnectionStringBuilder(connection.Value.GetString()).Database!;
                Assert.False(databases.TryGetValue(database, out var other), $"{service} and {other} share database {database}.");
                Assert.Equal($"recuro_{service.ToLowerInvariant()}", database);
                databases[database] = service;
            }
        }
    }

    [Theory]
    [MemberData(nameof(ServiceCatalog.All), MemberType = typeof(ServiceCatalog))]
    public void Every_DbContext_inherits_the_tenant_aware_base(string service)
    {
        var contexts = ServiceCatalog.Load(service, "Infrastructure").GetTypes()
            .Where(t => !t.IsAbstract && typeof(DbContext).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(contexts);
        Assert.All(contexts, t => Assert.True(typeof(RecuroDbContext).IsAssignableFrom(t), $"{t.Name} must inherit RecuroDbContext."));
    }

    [Theory]
    [MemberData(nameof(ServiceCatalog.All), MemberType = typeof(ServiceCatalog))]
    public void Domain_entities_expose_no_public_setters(string service)
    {
        var offenders = ServiceCatalog.Load(service, "Domain").GetTypes()
            .Where(t => typeof(BuildingBlocks.Domain.Entity).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties().Where(p => p.SetMethod?.IsPublic == true).Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        Assert.Empty(offenders);
    }
}
