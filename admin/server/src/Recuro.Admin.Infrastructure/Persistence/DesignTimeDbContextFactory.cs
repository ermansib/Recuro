using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Recuro.Admin.Application.Abstractions;

namespace Recuro.Admin.Infrastructure.Persistence;

/// <summary>Used only by "dotnet ef" to create PostgreSQL migrations; never at runtime.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AdminDbContext>
{
    public AdminDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql("Host=localhost;Database=recuro_admin_design", npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .Options;
        return new AdminDbContext(options, new NoTenant());
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid? TenantId => null;
    }
}
