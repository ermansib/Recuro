using NetArchTest.Rules;
using Recuro.Admin.Application;
using Recuro.Admin.Domain.Common;

namespace Recuro.Admin.ArchitectureTests;

/// <summary>Clean Architecture: dependencies point inward only.</summary>
public class LayerTests
{
    [Fact]
    public void Domain_depends_on_nothing_else()
    {
        var result = Types.InAssembly(typeof(Entity).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Recuro.Admin.Application",
                "Recuro.Admin.Infrastructure",
                "Recuro.Admin.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_does_not_know_about_infrastructure_or_web()
    {
        var result = Types.InAssembly(typeof(DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Recuro.Admin.Infrastructure",
                "Recuro.Admin.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
