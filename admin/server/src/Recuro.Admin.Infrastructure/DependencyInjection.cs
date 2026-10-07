using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Infrastructure.Persistence;
using Recuro.Admin.Infrastructure.Persistence.Seed;
using Recuro.Admin.Infrastructure.Repositories;

namespace Recuro.Admin.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "AdminDb";

    /// <summary>
    /// Registers persistence. Database:Provider picks "Postgres" (default, production) or "Sqlite"
    /// (tests and quick local runs without a database server).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = configuration["Database:Provider"] ?? "Postgres";
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        services.AddDbContext<AdminDbContext>(options =>
        {
            if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(connectionString);
            }
            else
            {
                options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"));
            }
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AdminDbContext>());
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IThemePresetRepository, ThemePresetRepository>();
        services.AddScoped<IScreenCatalog, ScreenCatalog>();
        services.AddScoped<IScreenConfigurationRepository, ScreenConfigurationRepository>();
        services.AddScoped<DatabaseSeeder>();
        services.AddSingleton<IClock, SystemClock>();
        return services;
    }

    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
