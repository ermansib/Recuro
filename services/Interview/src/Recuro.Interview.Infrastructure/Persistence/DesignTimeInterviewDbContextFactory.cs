using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Recuro.BuildingBlocks.Infrastructure.Persistence;

namespace Recuro.Interview.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API.</summary>
internal sealed class DesignTimeInterviewDbContextFactory : IDesignTimeDbContextFactory<InterviewDbContext>
{
    public InterviewDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<InterviewDbContext>();
        RecuroNpgsql.Configure(builder, "Host=localhost;Database=recuro_interview", typeof(InterviewDbContext).Assembly.GetName().Name!);
        return new InterviewDbContext(builder.Options, RecuroNpgsql.DesignTimeTenant, NoDomainEvents.Instance);
    }
}
