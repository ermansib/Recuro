using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Candidate.Infrastructure.Pii;

namespace Recuro.Candidate.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API. The throwaway key never encrypts real data.</summary>
internal sealed class DesignTimeCandidateDbContextFactory : IDesignTimeDbContextFactory<CandidateDbContext>
{
    public CandidateDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<CandidateDbContext>();
        RecuroNpgsql.Configure(builder, "Host=localhost;Database=recuro_candidate", typeof(CandidateDbContext).Assembly.GetName().Name!);
        var key = Convert.ToBase64String(new byte[32]);
        var cipher = new AesGcmPiiCipher(Options.Create(new PiiOptions { ActiveKeyId = "design", Keys = { ["design"] = key }, BlindIndexKey = key }));
        return new CandidateDbContext(builder.Options, RecuroNpgsql.DesignTimeTenant, NoDomainEvents.Instance, cipher);
    }
}
