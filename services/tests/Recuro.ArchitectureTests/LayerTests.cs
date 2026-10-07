using NetArchTest.Rules;

namespace Recuro.ArchitectureTests;

/// <summary>Clean Architecture inside each service: dependencies point inward only.</summary>
public class LayerTests
{
    private static readonly string[] Frameworks =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "RabbitMQ",
        "StackExchange.Redis",
        "Yarp",
    ];

    [Fact]
    public void At_least_one_service_is_checked() => Assert.NotEmpty(ServiceCatalog.Names);

    [Theory]
    [MemberData(nameof(ServiceCatalog.All), MemberType = typeof(ServiceCatalog))]
    public void Domain_depends_only_on_the_shared_domain_kernel(string service)
    {
        var forbidden = Frameworks
            .Concat(OuterLayers(service, "Application", "Infrastructure", "Api"))
            .Concat([$"{ServiceCatalog.BuildingBlocks}.Application", $"{ServiceCatalog.BuildingBlocks}.Infrastructure", $"{ServiceCatalog.BuildingBlocks}.Web"]);

        AssertNoDependency(ServiceCatalog.Load(service, "Domain"), forbidden);
    }

    [Theory]
    [MemberData(nameof(ServiceCatalog.All), MemberType = typeof(ServiceCatalog))]
    public void Application_knows_nothing_about_infrastructure_or_the_web(string service)
    {
        var forbidden = Frameworks
            .Concat(OuterLayers(service, "Infrastructure", "Api"))
            .Concat([$"{ServiceCatalog.BuildingBlocks}.Infrastructure", $"{ServiceCatalog.BuildingBlocks}.Web"]);

        AssertNoDependency(ServiceCatalog.Load(service, "Application"), forbidden);
    }

    [Theory]
    [MemberData(nameof(ServiceCatalog.All), MemberType = typeof(ServiceCatalog))]
    public void Infrastructure_does_not_depend_on_the_api(string service)
    {
        AssertNoDependency(
            ServiceCatalog.Load(service, "Infrastructure"),
            OuterLayers(service, "Api").Concat(["Microsoft.AspNetCore", $"{ServiceCatalog.BuildingBlocks}.Web"]));
    }

    [Fact]
    public void Shared_kernel_layers_point_inward_too()
    {
        AssertNoDependency(
            typeof(BuildingBlocks.Domain.Entity).Assembly,
            Frameworks.Concat(["Recuro.BuildingBlocks.Application", "Recuro.BuildingBlocks.Infrastructure", "Recuro.BuildingBlocks.Web"]));
        AssertNoDependency(
            typeof(BuildingBlocks.Application.DependencyInjection).Assembly,
            Frameworks.Concat(["Recuro.BuildingBlocks.Infrastructure", "Recuro.BuildingBlocks.Web"]));
        AssertNoDependency(
            typeof(BuildingBlocks.Infrastructure.DependencyInjection).Assembly,
            ["Microsoft.AspNetCore", "Recuro.BuildingBlocks.Web", "Yarp"]);
    }

    private static IEnumerable<string> OuterLayers(string service, params string[] layers) =>
        layers.Select(layer => $"{ServiceCatalog.Prefix}{service}.{layer}");

    internal static void AssertNoDependency(System.Reflection.Assembly assembly, IEnumerable<string> forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden.ToArray()).GetResult();
        Assert.True(result.IsSuccessful, $"{assembly.GetName().Name} breaks the rule: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
