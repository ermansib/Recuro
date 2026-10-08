using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.Onboarding.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API.</summary>
internal sealed class DesignTimeOnboardingDbContextFactory : IDesignTimeDbContextFactory<OnboardingDbContext>
{
    public OnboardingDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<OnboardingDbContext>();
        RecuroNpgsql.Configure(builder, "Host=localhost;Database=recuro_onboarding", typeof(OnboardingDbContext).Assembly.GetName().Name!);
        return new OnboardingDbContext(builder.Options, RecuroNpgsql.DesignTimeTenant, NoDomainEvents.Instance);
    }
}
