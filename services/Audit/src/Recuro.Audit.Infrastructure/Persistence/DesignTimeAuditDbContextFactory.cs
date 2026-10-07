using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.Audit.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API.</summary>
internal sealed class DesignTimeAuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AuditDbContext>();
        RecuroNpgsql.Configure(builder, "Host=localhost;Database=recuro_audit", typeof(AuditDbContext).Assembly.GetName().Name!);
        return new AuditDbContext(builder.Options, RecuroNpgsql.DesignTimeTenant, NoDomainEvents.Instance);
    }
}
