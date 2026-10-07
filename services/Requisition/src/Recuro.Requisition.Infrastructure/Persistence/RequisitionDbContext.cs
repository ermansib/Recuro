using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Requisition.Domain.JobDescriptions;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Infrastructure.Persistence;

/// <summary>The requisition service's own database (recuro_requisition). No other service reads it.</summary>
public sealed class RequisitionDbContext(DbContextOptions<RequisitionDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<ManpowerRequisition> Requisitions => Set<ManpowerRequisition>();

    public DbSet<JobDescription> JobDescriptions => Set<JobDescription>();

    public DbSet<ReqIdSequence> ReqIdSequences => Set<ReqIdSequence>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RequisitionDbContext).Assembly);
}

/// <summary>Last REQ-ID number issued per tenant and year (RCU-REQ-002).</summary>
public sealed class ReqIdSequence : BuildingBlocks.Domain.ITenantOwned
{
    private ReqIdSequence()
    {
    }

    public Guid TenantId { get; private set; }

    public int Year { get; private set; }

    public int LastValue { get; private set; }
}
