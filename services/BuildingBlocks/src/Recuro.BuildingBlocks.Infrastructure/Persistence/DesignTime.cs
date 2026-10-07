using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;

namespace Recuro.BuildingBlocks.Infrastructure.Persistence;

/// <summary>Shared Npgsql settings, used at runtime and by each service's design-time factory.</summary>
public static class RecuroNpgsql
{
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string connectionString, string migrationsAssembly)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(migrationsAssembly)
                .MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
    }

    /// <summary>A tenant context with no tenant, for <c>dotnet ef</c> at design time.</summary>
    public static ITenantContext DesignTimeTenant { get; } = new ScopeContext();
}
