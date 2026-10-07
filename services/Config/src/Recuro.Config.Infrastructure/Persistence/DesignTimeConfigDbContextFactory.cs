using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.Config.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API.</summary>
internal sealed class DesignTimeConfigDbContextFactory : IDesignTimeDbContextFactory<ConfigDbContext>
{
    public ConfigDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ConfigDbContext>();
        RecuroNpgsql.Configure(builder, "Host=localhost;Database=recuro_config", typeof(ConfigDbContext).Assembly.GetName().Name!);
        return new ConfigDbContext(builder.Options, RecuroNpgsql.DesignTimeTenant, NoDomainEvents.Instance);
    }
}
