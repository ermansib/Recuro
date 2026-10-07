using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Workflow.Domain.Workflows;

namespace Recuro.Workflow.Infrastructure.Persistence;

/// <summary>The workflow service's own database (recuro_workflow). No other service reads it.</summary>
public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<WorkflowInstance> Instances => Set<WorkflowInstance>();

    public DbSet<ApprovalTask> Tasks => Set<ApprovalTask>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkflowDbContext).Assembly);
}
