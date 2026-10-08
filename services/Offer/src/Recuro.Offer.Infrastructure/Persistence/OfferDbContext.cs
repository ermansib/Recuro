using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Offer.Domain.Applications;
using Recuro.Offer.Domain.Bgv;
using Recuro.Offer.Domain.Letters;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Infrastructure.Persistence;

/// <summary>The offer service's own database (recuro_offer). No other service reads it.</summary>
public sealed class OfferDbContext(DbContextOptions<OfferDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<JobOffer> Offers => Set<JobOffer>();

    public DbSet<OfferDocument> Documents => Set<OfferDocument>();

    public DbSet<ApplicationTrack> ApplicationTracks => Set<ApplicationTrack>();

    public DbSet<BgvTrack> BgvTracks => Set<BgvTrack>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OfferDbContext).Assembly);
}
