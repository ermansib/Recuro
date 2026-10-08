using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.Bgv.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API.</summary>
internal sealed class DesignTimeBgvDbContextFactory : IDesignTimeDbContextFactory<BgvDbContext>
{
    public BgvDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<BgvDbContext>();
        RecuroNpgsql.Configure(builder, "Host=localhost;Database=recuro_bgv", typeof(BgvDbContext).Assembly.GetName().Name!);
        return new BgvDbContext(builder.Options, RecuroNpgsql.DesignTimeTenant, NoDomainEvents.Instance);
    }
}
