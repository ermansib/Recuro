using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.Offer.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API.</summary>
internal sealed class DesignTimeOfferDbContextFactory : IDesignTimeDbContextFactory<OfferDbContext>
{
    public OfferDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<OfferDbContext>();
        RecuroNpgsql.Configure(builder, "Host=localhost;Database=recuro_offer", typeof(OfferDbContext).Assembly.GetName().Name!);
        return new OfferDbContext(builder.Options, RecuroNpgsql.DesignTimeTenant, NoDomainEvents.Instance);
    }
}
