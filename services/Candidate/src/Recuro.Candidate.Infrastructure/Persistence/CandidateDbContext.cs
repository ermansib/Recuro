using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Candidate.Infrastructure.Persistence.Configurations;
using Recuro.Candidate.Infrastructure.Pii;
using CandidateEntity = Recuro.Candidate.Domain.Candidates.Candidate;

namespace Recuro.Candidate.Infrastructure.Persistence;

/// <summary>The candidate service's own database (recuro_candidate). No other service reads it.</summary>
public sealed class CandidateDbContext(
    DbContextOptions<CandidateDbContext> options,
    ITenantContext tenant,
    IDomainEventDispatcher domainEvents,
    IPiiCipher cipher)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<CandidateEntity> Candidates => Set<CandidateEntity>();

    // The cipher only holds configured keys, so the model EF caches per context type stays valid.
    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new CandidateConfiguration(cipher));
}
