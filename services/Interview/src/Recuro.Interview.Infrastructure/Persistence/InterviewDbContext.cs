using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Interview.Domain.Applications;
using Recuro.Interview.Domain.Interviews;
using Recuro.Interview.Domain.Selection;

namespace Recuro.Interview.Infrastructure.Persistence;

/// <summary>The interview service's own database (recuro_interview). No other service reads it.</summary>
public sealed class InterviewDbContext(DbContextOptions<InterviewDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<InterviewRound> Interviews => Set<InterviewRound>();

    public DbSet<Assessment> Assessments => Set<Assessment>();

    public DbSet<ApplicationTrack> ApplicationTracks => Set<ApplicationTrack>();

    public DbSet<SelectionDecision> Selections => Set<SelectionDecision>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InterviewDbContext).Assembly);
}
