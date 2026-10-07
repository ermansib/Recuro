using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.Identity.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API.</summary>
internal sealed class DesignTimeIdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<IdentityDbContext>();
        RecuroNpgsql.Configure(builder, "Host=localhost;Database=recuro_identity", typeof(IdentityDbContext).Assembly.GetName().Name!);
        return new IdentityDbContext(builder.Options, RecuroNpgsql.DesignTimeTenant, NoDomainEvents.Instance);
    }
}
