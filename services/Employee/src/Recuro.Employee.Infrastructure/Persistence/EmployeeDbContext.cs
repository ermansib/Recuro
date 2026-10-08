using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Employee.Domain.Ijp;
using Recuro.Employee.Domain.Intake;
using Recuro.Employee.Domain.Referrals;
using Recuro.Employee.Domain.Requisitions;

namespace Recuro.Employee.Infrastructure.Persistence;

/// <summary>The employee portal's own database (recuro_employee). No other service reads it.</summary>
public sealed class EmployeeDbContext(DbContextOptions<EmployeeDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<IjpPosting> IjpPostings => Set<IjpPosting>();

    public DbSet<IntakeRecord> IntakeRecords => Set<IntakeRecord>();

    public DbSet<InternalApplication> InternalApplications => Set<InternalApplication>();

    public DbSet<Referral> Referrals => Set<Referral>();

    public DbSet<SourcingGate> SourcingGates => Set<SourcingGate>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EmployeeDbContext).Assembly);
}

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API.</summary>
internal sealed class DesignTimeEmployeeDbContextFactory : IDesignTimeDbContextFactory<EmployeeDbContext>
{
    public EmployeeDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<EmployeeDbContext>();
        RecuroNpgsql.Configure(builder, "Host=localhost;Database=recuro_employee", typeof(EmployeeDbContext).Assembly.GetName().Name!);
        return new EmployeeDbContext(builder.Options, RecuroNpgsql.DesignTimeTenant, NoDomainEvents.Instance);
    }
}
