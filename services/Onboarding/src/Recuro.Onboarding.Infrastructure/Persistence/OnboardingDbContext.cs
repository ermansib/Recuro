using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Onboarding.Domain.Bgv;
using Recuro.Onboarding.Domain.Cases;

namespace Recuro.Onboarding.Infrastructure.Persistence;

/// <summary>The Onboarding service's own database (recuro_onboarding). No other service reads it.</summary>
public sealed class OnboardingDbContext(DbContextOptions<OnboardingDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<OnboardingCase> Cases => Set<OnboardingCase>();

    public DbSet<BgvTrack> BgvTracks => Set<BgvTrack>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OnboardingDbContext).Assembly);
}
