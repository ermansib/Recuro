using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Careers.Domain.Applications;
using Recuro.Careers.Domain.Postings;
using Recuro.Careers.Domain.Requisitions;

namespace Recuro.Careers.Infrastructure.Persistence;

/// <summary>The careers service's own database (recuro_careers). No other service reads it.</summary>
public sealed class CareersDbContext(DbContextOptions<CareersDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<JobPosting> Postings => Set<JobPosting>();

    public DbSet<PublicApplication> Applications => Set<PublicApplication>();

    public DbSet<SourcingGate> SourcingGates => Set<SourcingGate>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CareersDbContext).Assembly);
}

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API.</summary>
internal sealed class DesignTimeCareersDbContextFactory : IDesignTimeDbContextFactory<CareersDbContext>
{
    public CareersDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<CareersDbContext>();
        RecuroNpgsql.Configure(builder, "Host=localhost;Database=recuro_careers", typeof(CareersDbContext).Assembly.GetName().Name!);
        return new CareersDbContext(builder.Options, RecuroNpgsql.DesignTimeTenant, NoDomainEvents.Instance);
    }
}
